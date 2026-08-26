"""Analisis del archivo de origen con ffprobe.

Responsabilidad: averiguar *que* contiene el archivo (codecs, pistas, duracion)
y derivar la estrategia de procesamiento. No construye ni ejecuta comandos de
FFmpeg — de eso se encarga `transcoder`.
"""

from __future__ import annotations

import asyncio
import json
from dataclasses import dataclass
from enum import Enum
from pathlib import Path

from app.errors import FFmpegError

# Codecs que el navegador reproduce sin re-codificar.
BROWSER_VIDEO_CODECS = frozenset({"h264"})
BROWSER_AUDIO_CODECS = frozenset({"aac"})


class StreamStrategy(str, Enum):
    """Ruta de menor costo para servir el video."""

    REMUX = "remux"          # H.264: reempaquetar con -c:v copy
    TRANSCODE = "transcode"  # otro codec: re-codificar a H.264


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
        return (
            f"Video: {self.video_codec} {self.width}x{self.height} "
            f"({self.strategy.value})  |  Audio: {audio}  |  "
            f"Duracion: {self.duration:.0f}s"
        )


async def probe(path: Path) -> dict:
    """Ejecuta ffprobe y devuelve la metadata cruda (streams + format)."""
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
        raise FFmpegError("ffprobe", process.returncode, stderr.decode(errors="replace"))

    return json.loads(stdout.decode(errors="replace"))


def parse_probe(info: dict, audio_track: int = 0) -> SourceInfo:
    """Convierte la salida de ffprobe en un `SourceInfo`.

    `audio_track` es el indice dentro de las pistas de audio (0 = la primera).
    """
    streams = info.get("streams", [])
    video_streams = [s for s in streams if s.get("codec_type") == "video"]
    audio_streams = [s for s in streams if s.get("codec_type") == "audio"]

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
        video_codec=video.get("codec_name", "?"),
        width=video.get("width"),
        height=video.get("height"),
        duration=duration,
        audio_codec=audio.get("codec_name") if audio else None,
        audio_channels=audio.get("channels") if audio else None,
        audio_language=(audio or {}).get("tags", {}).get("language", "und"),
        audio_count=len(audio_streams),
    )


async def analyze(path: Path, audio_track: int = 0) -> SourceInfo:
    """Atajo: ffprobe + parseo en un solo paso."""
    return parse_probe(await probe(path), audio_track)
