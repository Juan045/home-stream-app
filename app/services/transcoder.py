"""Construccion y ejecucion de los comandos de FFmpeg.

Responsabilidad: traducir un `SourceInfo` + opciones en argumentos de FFmpeg, y
correr el proceso reportando progreso. No decide *donde* va la salida ni como se
presenta el progreso — eso lo define quien lo llama.
"""

from __future__ import annotations

import asyncio
import contextlib
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable

import structlog

from app.errors import FFmpegError
from app.services import playlist
from app.services.media_analyzer import AudioTrack, SourceInfo, StreamStrategy

log = structlog.get_logger("transcoder")

# Cuantas lineas de stderr se guardan para el reporte de error.
STDERR_TAIL_LINES = 40
TERMINATE_TIMEOUT = 5.0

ProgressCallback = Callable[[float], None]
"""Recibe los segundos de video ya procesados."""


@dataclass(frozen=True)
class TranscodeOptions:
    """Parametros de codificacion y empaquetado HLS."""

    hls_time: int = 6
    crf: int = 23
    preset: str = "veryfast"
    video_max_bitrate: str = "4000k"
    video_bufsize: str = "8000k"
    video_max_height: int = 1080
    audio_bitrate: str = "128k"
    audio_channels: int = 2
    force_transcode: bool = False


BASE_ARGS = [
    "-nostdin",
    "-hide_banner",
    "-nostats",
    "-loglevel", "error",
    "-progress", "pipe:1",
]

# El nucleo de la solucion: el timeline del stream tiene que ser absoluto.
#
# `-copyts` evita que FFmpeg rebasee los PTS a cero. Como el video, cada pista
# de audio y los subtitulos se generan en corridas separadas, es lo unico que
# garantiza que compartan el mismo origen de tiempo. Sin esto los subtitulos se
# desfasan y los segmentos de dos corridas distintas no son intercambiables.
#
# NO agregar `-output_ts_offset`: con `-copyts` los PTS ya salen absolutos y
# sumarlo otra vez los duplica. Tampoco `-start_at_zero`, que lo contradice.
COPY_TIMESTAMPS = ["-copyts"]

# `-muxdelay`/`-muxpreload` en cero evitan que el muxer agregue su preload por
# defecto, que correria el timeline unos cientos de milisegundos.
MUX_ARGS = [
    "-avoid_negative_ts", "disabled",
    "-muxdelay", "0",
    "-muxpreload", "0",
]

DISCARD_EXTRAS = ["-sn", "-dn", "-map_chapters", "-1"]


def video_copy_allowed(info: SourceInfo, options: TranscodeOptions) -> bool:
    """True si el video se puede reempaquetar sin re-codificar."""
    return not options.force_transcode and info.strategy is StreamStrategy.REMUX


def audio_copy_allowed(track: AudioTrack, options: TranscodeOptions) -> bool:
    """True si la pista de audio se puede copiar tal cual."""
    return (
        not options.force_transcode
        and track.is_browser_ready
        and track.channels == options.audio_channels
    )


def _libx264_args(options: TranscodeOptions) -> list[str]:
    """Encoding de video con cortes de segmento en multiplos exactos.

    `-force_key_frames` pone un keyframe en cada borde de segmento y
    `-sc_threshold 0` desactiva los keyframes por cambio de escena, que podrian
    correr los cortes. Asi las duraciones salen exactas sin necesidad de
    indexar los keyframes del origen.
    """
    return [
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
        "-sc_threshold", "0",
    ]


def _fmp4_output_args(output_dir: Path, hls_time: int) -> list[str]:
    """Salida HLS en fMP4.

    El `internal.m3u8` que escribe FFmpeg no se sirve nunca: solo se parsea
    para conocer las duraciones reales de los segmentos. La playlist del
    cliente se calcula en `playlist.py`.
    """
    return [
        "-f", "hls",
        "-hls_time", str(hls_time),
        "-hls_playlist_type", "vod",
        "-hls_segment_type", "fmp4",
        "-hls_fmp4_init_filename", playlist.INIT_NAME,
        "-hls_flags", "independent_segments",
        "-hls_segment_filename", str(output_dir / playlist.SEGMENT_TEMPLATE),
        "-y", str(output_dir / playlist.INTERNAL_PLAYLIST),
    ]


def build_video_args(
    source: Path,
    output_dir: Path,
    info: SourceInfo,
    options: TranscodeOptions,
) -> list[str]:
    """Arma el comando que segmenta la pista de video, sin audio.

    Una sola corrida por archivo, completa y sin `-ss`: las duraciones que
    reporte FFmpeg son la fuente de verdad de la playlist.
    """
    copy_video = video_copy_allowed(info, options)

    log.debug(
        "construyendo comando de video",
        copy_video=copy_video,
        preset=options.preset,
        crf=options.crf,
    )

    args = list(BASE_ARGS)
    args += COPY_TIMESTAMPS
    args += ["-i", str(source)]
    args += ["-map", "0:v:0", "-an", *DISCARD_EXTRAS]
    args += ["-c:v", "copy"] if copy_video else _libx264_args(options)
    args += MUX_ARGS
    args += _fmp4_output_args(output_dir, options.hls_time)
    return args


def build_audio_args(
    source: Path,
    output_dir: Path,
    info: SourceInfo,
    options: TranscodeOptions,
    audio_track: int,
) -> list[str]:
    """Arma el comando que segmenta una pista de audio, sin video.

    Corre a ~200x tiempo real, asi que todas las pistas se pueden generar de
    entrada: cuando el usuario cambia de idioma la rendition ya esta lista.
    """
    if audio_track >= len(info.audio_tracks):
        raise ValueError(
            f"Pista de audio {audio_track} inexistente "
            f"(el archivo tiene {len(info.audio_tracks)})"
        )

    selected = info.audio_tracks[audio_track]
    copy_audio = audio_copy_allowed(selected, options)

    log.debug(
        "construyendo comando de audio",
        audio_track=audio_track,
        copy_audio=copy_audio,
        codec=selected.codec,
    )

    args = list(BASE_ARGS)
    args += COPY_TIMESTAMPS
    args += ["-i", str(source)]
    args += ["-map", f"0:a:{audio_track}", "-vn", *DISCARD_EXTRAS]

    if copy_audio:
        args += ["-c:a", "copy"]
    else:
        args += [
            "-c:a", "aac",
            "-b:a", options.audio_bitrate,
            "-ac", str(options.audio_channels),
        ]

    args += MUX_ARGS
    args += _fmp4_output_args(output_dir, options.hls_time)
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


@dataclass
class ManagedFFmpeg:
    """Proceso FFmpeg que se puede matar externamente."""

    process: asyncio.subprocess.Process
    _task: asyncio.Task
    _killed: bool = field(default=False, init=False)

    async def kill(self) -> None:
        """Detiene el proceso: SIGTERM primero, SIGKILL si no responde."""
        self._killed = True
        if self.process.returncode is None:
            self.process.terminate()
            try:
                await asyncio.wait_for(self.process.wait(), timeout=TERMINATE_TIMEOUT)
            except asyncio.TimeoutError:
                self.process.kill()
                await self.process.wait()
        self._task.cancel()
        with contextlib.suppress(asyncio.CancelledError, FFmpegError):
            await self._task

    @property
    def is_running(self) -> bool:
        return not self._task.done()

    async def wait(self) -> None:
        """Espera a que termine. Lanza FFmpegError si fallo (no si fue killed)."""
        try:
            await self._task
        except (asyncio.CancelledError, FFmpegError):
            if not self._killed:
                raise


async def start_ffmpeg(
    args: list[str],
    on_progress: ProgressCallback | None = None,
) -> ManagedFFmpeg:
    """Inicia FFmpeg en background. Devuelve un handle para matar/esperar."""
    log.debug("iniciando ffmpeg (managed)", args=args[:6])
    process = await asyncio.create_subprocess_exec(
        "ffmpeg", *args,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
    )

    async def _run() -> None:
        stderr_lines: list[str] = []
        drainer = asyncio.create_task(_drain(process.stderr, stderr_lines))
        last_seconds: float | None = None

        try:
            while True:
                raw = await process.stdout.readline()
                if not raw:
                    break
                seconds = parse_progress_line(raw.decode(errors="replace"))
                if seconds is None or seconds == last_seconds:
                    continue
                last_seconds = seconds
                if on_progress is not None:
                    on_progress(seconds)
        finally:
            await process.wait()
            await drainer

        if process.returncode != 0:
            raise FFmpegError(
                "ffmpeg",
                process.returncode,
                "\n".join(stderr_lines[-STDERR_TAIL_LINES:]),
            )

    task = asyncio.create_task(_run())
    return ManagedFFmpeg(process=process, _task=task)
