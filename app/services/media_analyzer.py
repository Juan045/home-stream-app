"""Analisis del archivo de origen con ffprobe.

Responsabilidad: averiguar *que* contiene el archivo (codecs, pistas, duracion)
y derivar la estrategia de procesamiento. No construye ni ejecuta comandos de
FFmpeg — de eso se encarga `transcoder`.
"""

from __future__ import annotations

import asyncio
import json
from dataclasses import asdict, dataclass, replace
from enum import Enum
from pathlib import Path
from typing import Iterable

import structlog

from app.errors import FFmpegError
from app.services.color import is_hdr_transfer

log = structlog.get_logger("media_analyzer")

# Codecs que el navegador reproduce sin re-codificar.
BROWSER_VIDEO_CODECS = frozenset({"h264"})
BROWSER_AUDIO_CODECS = frozenset({"aac"})


class StreamStrategy(str, Enum):
    """Ruta de menor costo para servir el video."""

    REMUX = "remux"          # H.264: reempaquetar con -c:v copy
    TRANSCODE = "transcode"  # otro codec: re-codificar a H.264


@dataclass(frozen=True)
class AudioTrack:
    """Metadata de una pista de audio.

    `ignore` es el unico campo que no dicta ffprobe: lo marca el usuario desde
    la ficha para que la pista no se genere. Va adentro de la pista y no en una
    lista aparte porque la decision es *por pista*, y dos listas paralelas hay
    que mantenerlas alineadas por indice a mano.
    """

    index: int
    codec: str
    channels: int
    language: str
    title: str
    profile: str | None = None  # "LC", "HE-AAC"...: define el string CODECS
    ignore: bool = False  # True si no se debe incluir en el stream HLS

    @property
    def is_browser_ready(self) -> bool:
        return self.codec in BROWSER_AUDIO_CODECS


@dataclass(frozen=True)
class SubtitleTrack:
    """Metadata de una pista de subtitulos."""

    index: int
    codec: str
    language: str
    title: str
    ignore: bool = False  # True si no se debe incluir en el stream HLS


@dataclass(frozen=True)
class SourceInfo:
    """Datos del archivo de origen relevantes para armar el stream."""

    video_codec: str
    width: int | None
    height: int | None
    duration: float
    audio_codec: str | None
    audio_channels: int | None
    audio_language: str
    audio_count: int
    audio_tracks: tuple[AudioTrack, ...]
    subtitle_tracks: tuple[SubtitleTrack, ...]
    # Necesarios para declarar CODECS y BANDWIDTH en el master playlist.
    video_profile: str | None = None
    video_level: int | None = None
    bit_rate: int | None = None
    # Metadata de color de ffprobe. Se guardan para que la decision de
    # tone-mapping sobreviva en la ficha y en el manifest del cache.
    video_color_primaries: str | None = None
    video_color_transfer: str | None = None
    video_color_space: str | None = None

    @property
    def strategy(self) -> StreamStrategy:
        """REMUX si el video ya es H.264, TRANSCODE en cualquier otro caso."""
        if self.video_codec in BROWSER_VIDEO_CODECS:
            return StreamStrategy.REMUX
        return StreamStrategy.TRANSCODE

    @property
    def has_audio(self) -> bool:
        return self.audio_codec is not None

    @property
    def is_hdr(self) -> bool:
        """True si la transferencia del video identifica HDR10/PQ o HLG."""
        return is_hdr_transfer(self.video_color_transfer)

    @property
    def audio_is_browser_ready(self) -> bool:
        """True si la pista de audio elegida no necesita re-codificarse."""
        return self.audio_codec in BROWSER_AUDIO_CODECS

    def describe(self) -> str:
        """Resumen de una linea para logs y CLI."""
        audio = (
            f"{self.audio_codec} {self.audio_channels}ch "
            f"[{self.audio_language}] ({self.audio_count} pistas)"
            if self.has_audio
            else "sin audio"
        )
        subs = f"{len(self.subtitle_tracks)} pistas" if self.subtitle_tracks else "ninguno"
        return (
            f"Video: {self.video_codec} {self.width}x{self.height} "
            f"({self.strategy.value})  |  Audio: {audio}  |  "
            f"Subs: {subs}  |  Duracion: {self.duration:.0f}s"
        )


def with_ignored(
    info: SourceInfo,
    *,
    audio: Iterable[int] | None = None,
    subtitles: Iterable[int] | None = None,
) -> SourceInfo:
    """Copia del `SourceInfo` con los `ignore` puestos segun los indices dados.

    Cada lista **reemplaza** al estado anterior de su clase de pista: lo que no
    esta en ella queda en `ignore=False`. Asi el formulario manda lo que quedo
    destildado y no tiene que llevar la cuenta de lo que cambio.

    `None` deja esa clase de pista intacta, que es como se toca solo el audio
    sin pisar los subtitulos.

    Un indice que no existe no hace nada: la lista se usa para *marcar* las
    pistas que ffprobe encontro, nunca para indexarlas.
    """
    changes: dict[str, tuple] = {}

    if audio is not None:
        ignored = set(audio)
        changes["audio_tracks"] = tuple(
            replace(track, ignore=track.index in ignored)
            for track in info.audio_tracks
        )

    if subtitles is not None:
        ignored = set(subtitles)
        changes["subtitle_tracks"] = tuple(
            replace(track, ignore=track.index in ignored)
            for track in info.subtitle_tracks
        )

    return replace(info, **changes) if changes else info


def to_dict(info: SourceInfo) -> dict:
    """Serializa un `SourceInfo` para guardarlo en el manifest del cache."""
    return asdict(info)


def from_dict(data: dict) -> SourceInfo:
    """Rehidrata un `SourceInfo` guardado, sin volver a correr ffprobe."""
    fields = dict(data)
    fields["audio_tracks"] = tuple(AudioTrack(**t) for t in data.get("audio_tracks", ()))
    fields["subtitle_tracks"] = tuple(
        SubtitleTrack(**t) for t in data.get("subtitle_tracks", ())
    )
    return SourceInfo(**fields)


async def probe(path: Path) -> dict:
    """Ejecuta ffprobe y devuelve la metadata cruda (streams + format)."""
    log.debug("ejecutando ffprobe", path=str(path))
    process = await asyncio.create_subprocess_exec(
        "ffprobe",
        "-v", "quiet",
        "-print_format", "json",
        "-show_streams",
        "-show_format",
        str(path),
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
    )
    stdout, stderr = await process.communicate()

    if process.returncode != 0:
        log.error("ffprobe fallo", returncode=process.returncode, path=str(path))
        raise FFmpegError("ffprobe", process.returncode, stderr.decode(errors="replace"))

    data = json.loads(stdout.decode(errors="replace"))
    stream_count = len(data.get("streams", []))
    log.debug("ffprobe completo", path=str(path), streams=stream_count)
    return data


def _as_int(value) -> int | None:
    """Convierte un campo de ffprobe a int, o None si no es utilizable."""
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def _parse_audio_tracks(streams: list[dict]) -> tuple[AudioTrack, ...]:
    return tuple(
        AudioTrack(
            index=i,
            codec=s.get("codec_name", "?"),
            channels=s.get("channels", 0),
            language=s.get("tags", {}).get("language", "und"),
            title=s.get("tags", {}).get("title", ""),
            profile=s.get("profile"),
        )
        for i, s in enumerate(streams)
    )


def _parse_subtitle_tracks(streams: list[dict]) -> tuple[SubtitleTrack, ...]:
    return tuple(
        SubtitleTrack(
            index=i,
            codec=s.get("codec_name", "?"),
            language=s.get("tags", {}).get("language", "und"),
            title=s.get("tags", {}).get("title", ""),
        )
        for i, s in enumerate(streams)
    )


def parse_probe(info: dict, audio_track: int = 0) -> SourceInfo:
    """Convierte la salida de ffprobe en un `SourceInfo`.

    `audio_track` es el indice dentro de las pistas de audio (0 = la primera).
    """
    streams = info.get("streams", [])
    video_streams = [s for s in streams if s.get("codec_type") == "video"]
    audio_streams = [s for s in streams if s.get("codec_type") == "audio"]
    subtitle_streams = [s for s in streams if s.get("codec_type") == "subtitle"]

    if not video_streams:
        raise ValueError("El archivo no tiene pistas de video")

    if audio_streams and audio_track >= len(audio_streams):
        raise ValueError(
            f"Pista de audio {audio_track} inexistente "
            f"(el archivo tiene {len(audio_streams)})"
        )

    video = video_streams[0]
    audio = audio_streams[audio_track] if audio_streams else None

    try:
        duration = float(info.get("format", {}).get("duration", 0.0))
    except (TypeError, ValueError):
        duration = 0.0

    return SourceInfo(
        video_profile=video.get("profile"),
        video_level=_as_int(video.get("level")),
        bit_rate=_as_int(info.get("format", {}).get("bit_rate")),
        video_color_primaries=video.get("color_primaries"),
        video_color_transfer=video.get("color_transfer"),
        video_color_space=video.get("color_space"),
        video_codec=video.get("codec_name", "?"),
        width=video.get("width"),
        height=video.get("height"),
        duration=duration,
        audio_codec=audio.get("codec_name") if audio else None,
        audio_channels=audio.get("channels") if audio else None,
        audio_language=(audio or {}).get("tags", {}).get("language", "und"),
        audio_count=len(audio_streams),
        audio_tracks=_parse_audio_tracks(audio_streams),
        subtitle_tracks=_parse_subtitle_tracks(subtitle_streams),
    )


async def analyze(path: Path, audio_track: int = 0) -> SourceInfo:
    """Atajo: ffprobe + parseo en un solo paso."""
    info = parse_probe(await probe(path), audio_track)
    log.info(
        "analisis completo",
        video_codec=info.video_codec,
        strategy=info.strategy.value,
        resolution=f"{info.width}x{info.height}",
        duration=round(info.duration, 1),
        audio_tracks=len(info.audio_tracks),
        subtitle_tracks=len(info.subtitle_tracks),
        audio_codec=info.audio_codec,
    )
    return info
