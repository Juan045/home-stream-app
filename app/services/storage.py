"""Gestion del directorio de salida (segmentos HLS y manifest)."""

from __future__ import annotations

import shutil
from dataclasses import dataclass
from pathlib import Path

import structlog

log = structlog.get_logger("storage")

SEGMENT_GLOB = "*.ts"
MANIFEST_NAME = "master.m3u8"


@dataclass(frozen=True)
class SegmentStats:
    """Resumen de lo que hay en el directorio de salida."""

    count: int
    total_bytes: int

    @property
    def total_mb(self) -> float:
        return self.total_bytes / 1024 / 1024


def prepare_output_dir(path: Path, keep: bool = False) -> Path:
    """Crea el directorio de salida y, salvo `keep`, lo deja vacio."""
    log.info("preparando directorio de salida", path=str(path), keep=keep)
    path.mkdir(parents=True, exist_ok=True)
    if not keep:
        clear_directory(path)
    return path


def clear_directory(path: Path) -> None:
    """Vacia el directorio sin borrarlo (puede ser un bind mount de Docker)."""
    entries = list(path.iterdir())
    if entries:
        log.debug("limpiando directorio", path=str(path), entries=len(entries))
    for entry in entries:
        if entry.is_dir() and not entry.is_symlink():
            shutil.rmtree(entry)
        else:
            entry.unlink()


def segment_stats(path: Path) -> SegmentStats:
    """Cuenta los segmentos generados y su tamano total."""
    segments = sorted(path.glob(SEGMENT_GLOB))
    return SegmentStats(
        count=len(segments),
        total_bytes=sum(s.stat().st_size for s in segments),
    )
