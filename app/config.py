"""Configuracion centralizada via variables de entorno."""

from __future__ import annotations

from functools import lru_cache
from pathlib import Path

from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    """Parametros del servidor. Se sobreescriben con variables de entorno."""

    HOST: str = "0.0.0.0"
    PORT: int = 8000
    OUTPUT_DIR: Path = Path("output")
    MEDIA_ROOT: Path | None = None
    HLS_TIME: int = 6
    FFMPEG_CRF: int = 23
    FFMPEG_PRESET: str = "veryfast"
    AUDIO_BITRATE: str = "128k"
    AUDIO_CHANNELS: int = 2

    model_config = {"env_prefix": "SM_"}


@lru_cache
def get_settings() -> Settings:
    return Settings()
