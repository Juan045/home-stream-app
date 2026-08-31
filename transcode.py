#!/usr/bin/env python3
"""CLI: genera los artefactos HLS de un video y opcionalmente los sirve.

Produce lo mismo que el servidor —segmentos de video, una pista de audio por
idioma, subtitulos WebVTT y las playlists— pero en una sola pasada y sin API.
La diferencia con el modo servidor es que aca se espera a que el build termine
antes de escribir las playlists, en vez de servirlas creciendo.

Uso:
    python transcode.py /ruta/al/video.mkv
    python transcode.py /ruta/al/video.mkv --serve
    python transcode.py /ruta/al/video.mkv --audio-tracks 0,2
"""

from __future__ import annotations

import argparse
import asyncio
import os
import shutil
import sys
import threading
import traceback
from pathlib import Path

import structlog

from app.errors import FFmpegError
from app.log import setup as setup_logging
from app.services import static_server
from app.services.asset_builder import VIDEO_KEY, Asset, AssetBuilder
from app.services.asset_store import AssetStore
from app.services.transcoder import TranscodeOptions

log = structlog.get_logger("cli")

REQUIRED_BINARIES = ("ffmpeg", "ffprobe")

# Cada cuantos por ciento se loguea el progreso cuando no hay TTY.
LOG_PROGRESS_STEP = 1.0


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Genera los artefactos HLS de un video (video, audios, subtitulos)."
    )
    parser.add_argument("source", type=Path, help="Ruta del archivo de video")
    parser.add_argument("-o", "--output", type=Path, default=Path("output"),
                        help="Directorio de cache (default: output/)")
    parser.add_argument("--audio-tracks", default=None,
                        help="Indices de pistas de audio separados por coma "
                             "(default: todas)")
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
    parser.add_argument("--clean", action="store_true",
                        help="Vacia el cache antes de empezar")
    parser.add_argument("--serve", action="store_true",
                        help="Sirve el directorio del proyecto por HTTP al terminar")
    parser.add_argument("--port", type=int, default=8000,
                        help="Puerto del servidor estatico (default: 8000)")
    parser.add_argument("--debug", action="store_true",
                        help="Log detallado en consola (nivel DEBUG)")
    return parser.parse_args(argv)


def options_from_args(args: argparse.Namespace) -> TranscodeOptions:
    """Traduce los argumentos de la CLI a opciones del transcoder."""
    return TranscodeOptions(
        hls_time=args.hls_time,
        crf=args.crf,
        preset=args.preset,
        audio_bitrate=args.audio_bitrate,
        audio_channels=args.audio_channels,
        force_transcode=args.force_transcode,
    )


def parse_audio_tracks(value: str | None) -> set[int] | None:
    """`"0,2"` -> `{0, 2}`. None significa todas las pistas."""
    if not value:
        return None
    try:
        return {int(part) for part in value.split(",") if part.strip()}
    except ValueError:
        raise SystemExit(f"ERROR: --audio-tracks invalido: {value!r}")


def make_progress_printer(duration: float, interactive: bool | None = None):
    """Devuelve un callback que imprime el progreso del build de video.

    En una terminal reescribe siempre la misma linea con `\\r`. Sin TTY (por
    ejemplo `docker logs`) eso genera una linea infinita: ahi imprime una linea
    nueva cada `LOG_PROGRESS_STEP` por ciento.
    """
    if interactive is None:
        interactive = sys.stdout.isatty()
    last_step = -1.0

    def report(artifact: str, seconds: float) -> None:
        nonlocal last_step

        # El audio corre muchisimo mas rapido; el video es el que marca el paso.
        if artifact != VIDEO_KEY:
            return

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


def describe(asset: Asset) -> str:
    """Resumen de lo que se genero."""
    subs = len(asset.subtitles)
    return (
        f"  {asset.info.describe()}\n"
        f"  Pistas de audio generadas: {len(asset.audio)}  |  "
        f"Subtitulos extraidos: {subs}"
    )


def announce_server(port: int, master: Path) -> None:
    """Imprime las URLs utiles del servidor estatico."""
    root = Path(__file__).parent.resolve()
    print(f"\nServidor: http://localhost:{port}")

    url = static_server.manifest_url(root, master.parent)
    if url is None:
        print("AVISO: el directorio de salida esta fuera del proyecto y no se sirve.")
    else:
        print(f"Player:   {static_server.player_url(port, url)}")
    print()


async def build(args: argparse.Namespace) -> tuple[Asset, Path]:
    """Corre el build completo y deja las playlists escritas."""
    source = args.source.expanduser().resolve()
    store = AssetStore(root=args.output.expanduser().resolve())
    store.root.mkdir(parents=True, exist_ok=True)
    if args.clean:
        store.clear()

    builder = AssetBuilder(
        store=store,
        options=options_from_args(args),
        audio_tracks=parse_audio_tracks(args.audio_tracks),
    )

    print(f"Origen: {source}")
    asset = await builder.open(source)
    print(describe(asset))

    # `open` vuelve apenas hay con que arrancar; la CLI quiere el archivo entero.
    builder.on_progress = make_progress_printer(asset.info.duration)
    await builder.wait_for_builds()
    print()

    if asset.status == "failed":
        raise SystemExit(f"ERROR: el build fallo:\n{asset.error}")

    master = builder.write_playlists(asset.id)
    print(f"Listo: {store.size_bytes(asset.id) / 1024 / 1024:.1f} MB en {asset.paths.root}")
    return asset, master


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

    _, master = await build(args)

    if args.serve:
        server = static_server.start_server(Path(__file__).parent.resolve(), args.port)
        announce_server(args.port, master)
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
