#!/usr/bin/env python3
"""CLI: transcodifica un video a H.264/AAC y lo empaqueta como HLS.

MVP sin API: la ruta del video se pasa por linea de comandos y la salida
(master.m3u8 + segmentos .ts) queda en un directorio local que se puede servir
como archivos estaticos.

Este modulo solo se ocupa de la interfaz de linea de comandos: parsear los
argumentos, orquestar los servicios de `app.services`, imprimir el progreso y
reportar las excepciones. La logica vive en los servicios.

Uso:
    python transcode.py /ruta/al/video.mkv
    python transcode.py /ruta/al/video.mkv --serve
"""

from __future__ import annotations

import argparse
import asyncio
import json
import os
import shutil
import sys
import threading
import traceback
from pathlib import Path

import structlog

from app.errors import FFmpegError
from app.log import setup as setup_logging
from app.services import static_server, storage
from app.services.media_analyzer import SourceInfo, analyze
from app.services.transcoder import TranscodeOptions, build_args, extract_subtitle, run_ffmpeg

log = structlog.get_logger("cli")

REQUIRED_BINARIES = ("ffmpeg", "ffprobe")

# Cada cuantos por ciento se loguea el progreso cuando no hay TTY.
LOG_PROGRESS_STEP = 1.0


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Transcodifica un video a H.264/AAC y genera un stream HLS."
    )
    parser.add_argument("source", type=Path, help="Ruta del archivo de video")
    parser.add_argument("-o", "--output", type=Path, default=Path("output"),
                        help="Directorio de salida (default: output/)")
    parser.add_argument("--audio-track", type=int, default=0,
                        help="Indice de la pista de audio a usar (default: 0)")
    parser.add_argument("--hls-time", type=int, default=6,
                        help="Duracion de cada segmento en segundos (default: 6)")
    parser.add_argument("--crf", type=int, default=23,
                        help="Calidad de libx264, 18-28 (default: 23)")
    parser.add_argument("--preset", default="veryfast",
                        help="Preset de libx264 (default: veryfast)")
    parser.add_argument("--audio-bitrate", default="128k",
                        help="Bitrate del audio AAC (default: 128k)")
    parser.add_argument("--audio-channels", type=int, default=2,
                        help="Canales de audio de salida (default: 2)")
    parser.add_argument("--force-transcode", action="store_true",
                        help="Re-codifica aunque el origen ya sea H.264/AAC")
    parser.add_argument("--keep", action="store_true",
                        help="No borrar el contenido previo del directorio de salida")
    parser.add_argument("--serve", action="store_true",
                        help="Sirve el directorio del proyecto por HTTP mientras procesa")
    parser.add_argument("--port", type=int, default=8000,
                        help="Puerto del servidor estatico (default: 8000)")
    parser.add_argument("--debug", action="store_true",
                        help="Log detallado en consola (nivel DEBUG)")
    return parser.parse_args(argv)


def options_from_args(args: argparse.Namespace) -> TranscodeOptions:
    """Traduce los argumentos de la CLI a opciones del transcoder."""
    return TranscodeOptions(
        audio_track=args.audio_track,
        hls_time=args.hls_time,
        crf=args.crf,
        preset=args.preset,
        audio_bitrate=args.audio_bitrate,
        audio_channels=args.audio_channels,
        force_transcode=args.force_transcode,
    )


def make_progress_printer(duration: float, interactive: bool | None = None):
    """Devuelve un callback que imprime el progreso.

    En una terminal reescribe siempre la misma linea con `\\r`. Sin TTY (por
    ejemplo `docker logs`) eso genera una linea infinita: ahi imprime una linea
    nueva cada `LOG_PROGRESS_STEP` por ciento.
    """
    if interactive is None:
        interactive = sys.stdout.isatty()
    last_step = -1.0

    def report(seconds: float) -> None:
        nonlocal last_step

        if duration <= 0:
            text = f"  Procesado: {seconds:.0f}s"
            pct = None
        else:
            pct = min(seconds / duration * 100, 100.0)
            text = f"  Progreso: {pct:5.1f}%  ({seconds:.0f}s / {duration:.0f}s)"

        if interactive:
            print("\r" + text, end="", flush=True)
            return

        step = pct // LOG_PROGRESS_STEP if pct is not None else seconds // 60
        if step > last_step:
            last_step = step
            print(text, flush=True)

    return report


def missing_binaries() -> list[str]:
    return [b for b in REQUIRED_BINARIES if shutil.which(b) is None]


async def extract_all_subtitles(
    source: Path, output_dir: Path, info: SourceInfo,
) -> dict[int, Path]:
    """Extrae todas las pistas de subtitulos a WebVTT. Retorna {indice: ruta}."""
    if not info.subtitle_tracks:
        return {}

    results: dict[int, Path] = {}
    for track in info.subtitle_tracks:
        vtt_path = output_dir / "subtitles" / f"sub_{track.index}_{track.language}.vtt"
        try:
            await extract_subtitle(source, vtt_path, track.index)
            results[track.index] = vtt_path
        except FFmpegError as exc:
            print(
                f"  AVISO: no se pudo extraer subtitulo {track.index} "
                f"({track.language}, {track.codec}): {exc.stderr.splitlines()[-1] if exc.stderr else 'error desconocido'}",
                file=sys.stderr,
            )
    return results


def write_metadata(
    output_dir: Path, info: SourceInfo, extracted: dict[int, Path],
    server_root: Path,
) -> Path:
    """Escribe metadata.json con las pistas de subtitulos extraidas."""
    tracks = []
    for track in info.subtitle_tracks:
        if track.index not in extracted:
            continue
        rel = extracted[track.index].relative_to(server_root).as_posix()
        tracks.append({
            "index": track.index,
            "language": track.language,
            "title": track.title,
            "url": f"/{rel}",
        })

    meta_path = output_dir / "metadata.json"
    meta_path.write_text(
        json.dumps({"subtitle_tracks": tracks}, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    return meta_path


def announce_server(port: int, output_dir: Path) -> None:
    """Imprime las URLs utiles del servidor estatico."""
    root = Path(__file__).parent.resolve()
    print(f"\nServidor: http://localhost:{port}")

    manifest = static_server.manifest_url(root, output_dir)
    if manifest is None:
        print("AVISO: el directorio de salida esta fuera del proyecto y no se sirve.")
    else:
        print(f"Player:   {static_server.player_url(port, manifest)}")
    print()


async def transcode(source: Path, output_dir: Path, info: SourceInfo,
                    options: TranscodeOptions) -> None:
    """Corre FFmpeg mostrando progreso y el resumen de segmentos al terminar."""
    args = build_args(source, output_dir, info, options)
    print("Comando: ffmpeg " + " ".join(args))

    try:
        await run_ffmpeg(args, on_progress=make_progress_printer(info.duration))
    finally:
        print()
        stats = storage.segment_stats(output_dir)
        if stats.count:
            print(f"Listo: {stats.count} segmentos, "
                  f"{stats.total_mb:.1f} MB en {output_dir}")


async def main(argv: list[str]) -> int:
    args = parse_args(argv)
    debug = args.debug or os.environ.get("DEBUG", "").lower() in ("1", "true", "yes")
    setup_logging(debug=debug)

    log.info("inicio", source=str(args.source), output=str(args.output), debug=debug)

    if missing := missing_binaries():
        log.error("binarios faltantes", missing=missing)
        print(f"ERROR: falta(n) en el PATH: {', '.join(missing)}", file=sys.stderr)
        return 1

    source = args.source.expanduser().resolve()
    if not source.is_file():
        log.error("archivo no encontrado", path=str(source))
        print(f"ERROR: el archivo no existe: {source}", file=sys.stderr)
        return 1

    output_dir = storage.prepare_output_dir(
        args.output.expanduser().resolve(), keep=args.keep
    )

    print(f"Origen: {source}")
    info = await analyze(source, args.audio_track)
    print(f"  {info.describe()}")

    server_root = Path(__file__).parent.resolve()
    extracted = await extract_all_subtitles(source, output_dir, info)
    if extracted:
        write_metadata(output_dir, info, extracted, server_root)
        print(f"  Subtitulos: {len(extracted)} pista(s) extraida(s)")
        log.info("subtitulos extraidos", count=len(extracted))

    server = None
    if args.serve:
        server = static_server.start_server(Path(__file__).parent.resolve(), args.port)
        announce_server(args.port, output_dir)

    log.info("iniciando transcodificacion", strategy=info.strategy.value)
    await transcode(source, output_dir, info, options_from_args(args))
    log.info("transcodificacion completa")

    if server is not None:
        print("Servidor activo. Ctrl+C para salir.")
        try:
            await asyncio.get_running_loop().run_in_executor(
                None, threading.Event().wait
            )
        finally:
            log.info("apagando servidor")
            server.shutdown()

    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(asyncio.run(main(sys.argv[1:])))
    except KeyboardInterrupt:
        print("\nInterrumpido por el usuario.", file=sys.stderr)
        raise SystemExit(130)
    except FFmpegError as exc:
        print(f"\n--- {exc.command} fallo (codigo {exc.returncode}) ---", file=sys.stderr)
        print(exc.stderr, file=sys.stderr)
        traceback.print_exc()
        raise SystemExit(1)
    except Exception:
        print("\n--- Excepcion no controlada ---", file=sys.stderr)
        traceback.print_exc()
        raise SystemExit(1)
