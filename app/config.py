"""Configuracion centralizada via variables de entorno."""

from __future__ import annotations

from functools import lru_cache
from pathlib import Path

from pydantic_settings import BaseSettings

GIB = 1024**3


class Settings(BaseSettings):
    """Parametros del servidor. Se sobreescriben con variables de entorno."""

    HOST: str = "0.0.0.0"
    PORT: int = 8000

    # Directorio de artefactos. Persiste entre arranques: es un cache, no un
    # temporal. Abrir dos veces la misma pelicula no vuelve a generar nada.
    CACHE_DIR: Path = Path("output")
    MEDIA_ROOT: Path | None = None

    HLS_TIME: int = 6
    FFMPEG_CRF: int = 23
    FFMPEG_PRESET: str = "veryfast"
    AUDIO_BITRATE: str = "128k"
    AUDIO_CHANNELS: int = 2

    # Cuantos FFmpeg pueden correr a la vez. El video de un asset y sus pistas
    # de audio cuentan por separado.
    MAX_CONCURRENT_FFMPEG: int = 3

    # Tope del cache. Al superarlo se borran los assets menos usados que no
    # tengan a nadie mirandolos.
    MAX_CACHE_SIZE: int = 50 * GIB

    SESSION_INACTIVE_AFTER: float = 120.0
    SESSION_EXPIRE_AFTER: float = 720.0
    CLEANUP_INTERVAL: float = 60.0

    model_config = {"env_prefix": "SM_"}


@lru_cache
def get_settings() -> Settings:
    return Settings()
