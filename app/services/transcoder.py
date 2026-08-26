"""Construccion y ejecucion de los comandos de FFmpeg.

Responsabilidad: traducir un `SourceInfo` + opciones en argumentos de FFmpeg, y
correr el proceso reportando progreso. No decide *donde* va la salida ni como se
presenta el progreso — eso lo define quien lo llama.
"""

from __future__ import annotations

import asyncio
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

import structlog

from app.errors import FFmpegError
from app.services.media_analyzer import SourceInfo, StreamStrategy

log = structlog.get_logger("transcoder")

# Cuantas lineas de stderr se guardan para el reporte de error.
STDERR_TAIL_LINES = 40

ProgressCallback = Callable[[float], None]
"""Recibe los segundos de video ya procesados."""


@dataclass(frozen=True)
class TranscodeOptions:
    """Parametros de codificacion y empaquetado HLS."""

    audio_track: int = 0
    hls_time: int = 6
    crf: int = 23
    preset: str = "veryfast"
    video_max_bitrate: str = "4000k"
    video_bufsize: str = "8000k"
    video_max_height: int = 1080
    audio_bitrate: str = "128k"
    audio_channels: int = 2
    force_transcode: bool = False


def build_args(
    source: Path,
    output_dir: Path,
    info: SourceInfo,
    options: TranscodeOptions,
) -> list[str]:
    """Arma los argumentos de FFmpeg segun los codecs del origen."""
    copy_video = not options.force_transcode and info.strategy is StreamStrategy.REMUX
    copy_audio = (
        not options.force_transcode
        and info.audio_is_browser_ready
        and info.audio_channels == options.audio_channels
    )

    log.debug(
        "construyendo comando ffmpeg",
        copy_video=copy_video,
        copy_audio=copy_audio,
        audio_track=options.audio_track,
        preset=options.preset,
        crf=options.crf,
    )

    args = [
        "-hide_banner",
        "-nostats",
        "-loglevel", "error",
        "-progress", "pipe:1",
        "-i", str(source),
        "-map", "0:v:0",
    ]

    if info.has_audio:
        args += ["-map", f"0:a:{options.audio_track}"]

    # Los subtitulos (y datos/capitulos) se descartan en este MVP.
    args += ["-sn", "-dn", "-map_chapters", "-1"]

    if copy_video:
        args += ["-c:v", "copy"]
    else:
        args += [
            "-c:v", "libx264",
            "-preset", options.preset,
            "-crf", str(options.crf),
            "-maxrate", options.video_max_bitrate,
            "-bufsize", options.video_bufsize,
            "-pix_fmt", "yuv420p",
            "-profile:v", "high",
            "-level", "4.1",
            "-vf", f"scale=-2:'min({options.video_max_height},ih)'",
            "-force_key_frames", f"expr:gte(t,n_forced*{options.hls_time})",
        ]

    if not info.has_audio:
        args += ["-an"]
    elif copy_audio:
        args += ["-c:a", "copy"]
    else:
        args += [
            "-c:a", "aac",
            "-b:a", options.audio_bitrate,
            "-ac", str(options.audio_channels),
        ]

    args += [
        "-f", "hls",
        "-hls_time", str(options.hls_time),
        "-hls_list_size", "0",
        "-hls_playlist_type", "event",       # permite reproducir mientras se genera
        "-hls_flags", "independent_segments",
        "-hls_segment_filename", str(output_dir / "segment_%05d.ts"),
        "-y",
        str(output_dir / "master.m3u8"),
    ]
    return args


def build_subtitle_args(
    source: Path,
    output_path: Path,
    subtitle_track: int,
) -> list[str]:
    """Arma los argumentos para extraer una pista de subtitulos a WebVTT."""
    return [
        "-hide_banner",
        "-nostats",
        "-loglevel", "error",
        "-i", str(source),
        "-map", f"0:s:{subtitle_track}",
        "-c:s", "webvtt",
        "-y",
        str(output_path),
    ]


async def extract_subtitle(
    source: Path,
    output_path: Path,
    subtitle_track: int,
) -> Path:
    """Extrae una pista de subtitulos a WebVTT. Devuelve la ruta del .vtt."""
    log.info("extrayendo subtitulo", track=subtitle_track, output=str(output_path))
    output_path.parent.mkdir(parents=True, exist_ok=True)
    args = build_subtitle_args(source, output_path, subtitle_track)
    await run_ffmpeg(args)
    log.debug("subtitulo extraido", track=subtitle_track, output=str(output_path))
    return output_path


def parse_progress_line(line: str) -> float | None:
    """Extrae los segundos procesados de una linea de `-progress`.

    FFmpeg emite `out_time_us` y `out_time_ms`, ambos en microsegundos.
    Devuelve None si la linea no reporta tiempo.
    """
    key, _, value = line.strip().partition("=")
    if key in ("out_time_us", "out_time_ms") and value.isdigit():
        return int(value) / 1_000_000
    return None


async def _drain(stream: asyncio.StreamReader, sink: list[str]) -> None:
    """Acumula stderr para poder mostrarlo si FFmpeg falla."""
    while True:
        line = await stream.readline()
        if not line:
            break
        sink.append(line.decode(errors="replace").rstrip())


async def run_ffmpeg(
    args: list[str],
    on_progress: ProgressCallback | None = None,
) -> None:
    """Ejecuta FFmpeg hasta el final. Lanza `FFmpegError` si termina mal."""
    log.debug("iniciando ffmpeg", args=args[:6])
    process = await asyncio.create_subprocess_exec(
        "ffmpeg", *args,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
    )

    stderr_lines: list[str] = []
    drainer = asyncio.create_task(_drain(process.stderr, stderr_lines))
    last_seconds: float | None = None

    try:
        while True:
            raw = await process.stdout.readline()
            if not raw:
                break
            seconds = parse_progress_line(raw.decode(errors="replace"))
            # FFmpeg emite out_time_us y out_time_ms con el mismo valor en cada
            # bloque: sin este filtro cada avance se reportaria dos veces.
            if seconds is None or seconds == last_seconds:
                continue
            last_seconds = seconds
            if on_progress is not None:
                on_progress(seconds)
    except asyncio.CancelledError:
        process.kill()
        raise
    finally:
        await process.wait()
        await drainer

    if process.returncode != 0:
        log.error("ffmpeg fallo", returncode=process.returncode, stderr_tail=stderr_lines[-3:])
        raise FFmpegError(
            "ffmpeg",
            process.returncode,
            "\n".join(stderr_lines[-STDERR_TAIL_LINES:]),
        )
    log.debug("ffmpeg terminado", returncode=0)
