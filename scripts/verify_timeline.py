#!/usr/bin/env python3
"""Verifica con archivos reales que el timeline generado es correcto.

Los tests unitarios comprueban que los comandos de FFmpeg se arman bien, pero
no que FFmpeg haga lo esperado. Este script corre las comprobaciones que solo
tienen sentido sobre artefactos ya generados.

Uso:
    python scripts/verify_timeline.py output/{asset_id}
    python scripts/verify_timeline.py output/{asset_id} --segment 20

Requiere ffprobe en el PATH.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
from pathlib import Path

TOLERANCE_SECONDS = 1.0
DURATION_TOLERANCE = 0.5


class Result:
    def __init__(self) -> None:
        self.failures: list[str] = []

    def check(self, ok: bool, name: str, detail: str = "") -> None:
        mark = "OK  " if ok else "FALLA"
        print(f"  [{mark}] {name}" + (f"  {detail}" if detail else ""))
        if not ok:
            self.failures.append(name)

    def skip(self, name: str, motivo: str) -> None:
        print(f"  [SKIP] {name}  {motivo}")


def ffprobe(*args: str) -> str:
    out = subprocess.run(
        ["ffprobe", "-v", "error", *args],
        capture_output=True, text=True, check=False,
    )
    if out.returncode != 0:
        raise RuntimeError(out.stderr.strip())
    return out.stdout.strip()


def extinf_durations(playlist: Path) -> list[float]:
    durations = []
    for line in playlist.read_text(encoding="utf-8").splitlines():
        if line.startswith("#EXTINF:"):
            durations.append(float(line[len("#EXTINF:"):].split(",")[0]))
    return durations


def first_packet_pts(track_dir: Path, index: int, stream: str) -> float | None:
    """Primer PTS del segmento. fMP4 necesita el init para decodificarse."""
    init = track_dir / "init.mp4"
    segment = track_dir / f"seg-{index:05d}.m4s"
    if not init.exists() or not segment.exists():
        return None

    with tempfile.NamedTemporaryFile(suffix=".mp4", delete=False) as tmp:
        tmp.write(init.read_bytes())
        tmp.write(segment.read_bytes())
        joined = Path(tmp.name)

    try:
        salida = ffprobe(
            "-select_streams", stream,
            "-show_entries", "packet=pts_time",
            "-of", "csv=p=0", str(joined),
        )
        valores = [v for v in salida.replace(",", "\n").splitlines() if v.strip()]
        return float(valores[0]) if valores else None
    finally:
        joined.unlink(missing_ok=True)


def first_frame_is_keyframe(track_dir: Path, index: int) -> bool | None:
    init = track_dir / "init.mp4"
    segment = track_dir / f"seg-{index:05d}.m4s"
    if not init.exists() or not segment.exists():
        return None

    with tempfile.NamedTemporaryFile(suffix=".mp4", delete=False) as tmp:
        tmp.write(init.read_bytes())
        tmp.write(segment.read_bytes())
        joined = Path(tmp.name)

    try:
        salida = ffprobe(
            "-select_streams", "v:0",
            "-show_entries", "frame=key_frame",
            "-of", "csv=p=0", str(joined),
        )
        primera = next((v for v in salida.replace(",", "\n").splitlines() if v.strip()), "")
        return primera == "1"
    finally:
        joined.unlink(missing_ok=True)


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("asset_dir", type=Path, help="output/{asset_id}")
    parser.add_argument("--segment", type=int, default=20,
                        help="Indice de segmento a inspeccionar (default: 20)")
    args = parser.parse_args(argv)

    asset = args.asset_dir.resolve()
    video_dir = asset / "video"
    if not video_dir.is_dir():
        print(f"ERROR: no parece un asset: {asset}", file=sys.stderr)
        return 1

    manifest = json.loads((asset / "manifest.json").read_text(encoding="utf-8"))
    duration = float(manifest["duration"])
    audio_dirs = sorted((asset / "audio").glob("*")) if (asset / "audio").is_dir() else []

    video_playlist = video_dir / "playlist.m3u8"
    if not video_playlist.exists():
        print("ERROR: falta video/playlist.m3u8 (correr la CLI, no la API)", file=sys.stderr)
        return 1

    durations = extinf_durations(video_playlist)
    n = args.segment
    esperado = sum(durations[:n])

    result = Result()
    print(f"\nAsset: {asset}")
    print(f"Duracion declarada: {duration:.1f}s  |  segmentos: {len(durations)}\n")

    # 1. PTS absoluto: el segmento N debe declarar su tiempo real, no cero.
    print("1. PTS absoluto del video")
    pts = first_packet_pts(video_dir, n, "v:0")
    if pts is None:
        result.skip("segmento presente", f"no existe seg-{n:05d}.m4s")
    else:
        ok = abs(pts - esperado) < TOLERANCE_SECONDS
        diagnostico = ""
        if not ok:
            if abs(pts) < TOLERANCE_SECONDS:
                diagnostico = "  -> falta -copyts"
            elif abs(pts - 2 * esperado) < TOLERANCE_SECONDS:
                diagnostico = "  -> se colo -output_ts_offset (offset duplicado)"
        result.check(ok, f"seg-{n:05d} arranca en ~{esperado:.1f}s",
                     f"PTS={pts:.3f}{diagnostico}")

    # 2. Las duraciones declaradas tienen que ser las reales.
    print("\n2. Suma de #EXTINF vs duracion del origen")
    total = sum(durations)
    result.check(abs(total - duration) < DURATION_TOLERANCE,
                 "la playlist no miente",
                 f"suma={total:.2f}s  origen={duration:.2f}s")

    # 3. Cada segmento tiene que empezar en keyframe.
    print("\n3. Keyframe al inicio del segmento")
    es_key = first_frame_is_keyframe(video_dir, n)
    if es_key is None:
        result.skip("keyframe", f"no existe seg-{n:05d}.m4s")
    else:
        result.check(es_key, f"seg-{n:05d} abre con keyframe")

    # 4. El audio tiene que caer en el mismo punto del timeline que el video.
    print("\n4. Alineacion audio/video")
    if pts is None or not audio_dirs:
        result.skip("alineacion", "sin video o sin pistas de audio")
    else:
        for audio_dir in audio_dirs:
            apts = first_packet_pts(audio_dir, n, "a:0")
            if apts is None:
                result.skip(f"audio/{audio_dir.name}", "segmento faltante")
                continue
            result.check(abs(apts - pts) < TOLERANCE_SECONDS,
                         f"audio/{audio_dir.name} alineado con el video",
                         f"PTS={apts:.3f} vs {pts:.3f}")

    # 5. El timeline no puede cambiar al cambiar de idioma.
    print("\n5. Playlists identicas entre idiomas")
    playlists = [d / "playlist.m3u8" for d in audio_dirs if (d / "playlist.m3u8").exists()]
    if len(playlists) < 2:
        result.skip("comparacion entre idiomas", "hay menos de dos pistas")
    else:
        base = playlists[0].read_text(encoding="utf-8")
        for otra in playlists[1:]:
            result.check(otra.read_text(encoding="utf-8") == base,
                         f"{otra.parent.name} identica a {playlists[0].parent.name}")

    print()
    if result.failures:
        print(f"FALLARON {len(result.failures)}: {', '.join(result.failures)}")
        return 1
    print("Todas las comprobaciones pasaron.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
