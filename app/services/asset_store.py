"""Identidad y layout en disco de los artefactos derivados de un archivo.

Un *asset* es un archivo de origen mas todo lo que se genero a partir de el:
segmentos de video, segmentos de audio por pista, subtitulos y el manifest con
su estado. Vive en el cache y sobrevive entre sesiones, asi que la segunda vez
que se abre la misma pelicula no hay nada que generar.

La identidad es `(ruta, mtime, tamano)`: si el archivo cambia, el asset_id
cambia y los artefactos viejos quedan hijos de nadie hasta que el GC los barra.
"""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import time
from dataclasses import dataclass
from pathlib import Path

import structlog

log = structlog.get_logger("asset_store")

ASSET_ID_LENGTH = 16

MANIFEST_NAME = "manifest.json"
ACCESS_MARKER = ".last-access"
VIDEO_DIR = "video"
AUDIO_DIR = "audio"
SUBS_DIR = "subs"


def asset_id_for(source: Path) -> str:
    """Identificador estable del archivo de origen.

    `normcase` para que en Windows la misma pelicula abierta con distinta
    capitalizacion no genere dos assets. `mtime_ns` y el tamano hacen que
    reemplazar el archivo invalide el cache solo.
    """
    stat = source.stat()
    normalized = os.path.normcase(str(source.resolve()))
    raw = f"{normalized}|{stat.st_mtime_ns}|{stat.st_size}"
    return hashlib.sha1(raw.encode("utf-8")).hexdigest()[:ASSET_ID_LENGTH]


@dataclass(frozen=True)
class AssetPaths:
    """Rutas de los artefactos de un asset dentro del cache."""

    root: Path

    @property
    def manifest(self) -> Path:
        return self.root / MANIFEST_NAME

    @property
    def video(self) -> Path:
        return self.root / VIDEO_DIR

    @property
    def subs(self) -> Path:
        return self.root / SUBS_DIR

    def audio(self, track: int) -> Path:
        return self.root / AUDIO_DIR / str(track)


class AssetStore:
    """Acceso al directorio de cache: layout, manifest, uso de disco y GC."""

    def __init__(self, root: Path) -> None:
        self._root = root

    @property
    def root(self) -> Path:
        return self._root

    def paths(self, asset_id: str) -> AssetPaths:
        return AssetPaths(root=self._root / asset_id)

    def locate(self, source: Path) -> tuple[str, AssetPaths]:
        """Devuelve el id y las rutas del asset correspondiente al origen."""
        asset_id = asset_id_for(source)
        return asset_id, self.paths(asset_id)

    def prepare(self, asset_id: str) -> AssetPaths:
        """Crea el arbol de directorios del asset y lo marca como usado."""
        paths = self.paths(asset_id)
        paths.video.mkdir(parents=True, exist_ok=True)
        paths.subs.mkdir(parents=True, exist_ok=True)
        (paths.root / AUDIO_DIR).mkdir(parents=True, exist_ok=True)
        self.touch(asset_id)
        return paths

    def prepare_audio(self, asset_id: str, track: int) -> Path:
        directory = self.paths(asset_id).audio(track)
        directory.mkdir(parents=True, exist_ok=True)
        return directory

    def exists(self, asset_id: str) -> bool:
        return self.paths(asset_id).manifest.exists()

    # --- Manifest -----------------------------------------------------------

    def read_manifest(self, asset_id: str) -> dict | None:
        """Lee el manifest, o None si no existe o quedo corrupto."""
        path = self.paths(asset_id).manifest
        try:
            return json.loads(path.read_text(encoding="utf-8"))
        except FileNotFoundError:
            return None
        except (json.JSONDecodeError, OSError) as exc:
            log.warning("manifest ilegible", asset_id=asset_id, error=str(exc))
            return None

    def is_pinned(self, asset_id: str) -> bool:
        """True si el asset lo genero una codificacion de biblioteca.

        Se lee del manifest y no de la memoria del builder a proposito: el
        pin tiene que sobrevivir a un reinicio. Si no, el primer GC despues de
        levantar el server se lleva horas de codificacion.
        """
        manifest = self.read_manifest(asset_id)
        return bool(manifest and manifest.get("pinned", False))

    def write_manifest(self, asset_id: str, data: dict) -> None:
        """Escribe el manifest de forma atomica.

        El builder lo reescribe mientras las requests lo leen: sin el rename
        atomico un `GET` puede toparse con un JSON a medio escribir.
        """
        path = self.paths(asset_id).manifest
        path.parent.mkdir(parents=True, exist_ok=True)
        temporary = path.with_suffix(".json.tmp")
        temporary.write_text(
            json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8"
        )
        os.replace(temporary, path)

    # --- Uso reciente y espacio ---------------------------------------------

    def touch(self, asset_id: str, when: float | None = None) -> None:
        """Marca el asset como usado ahora (orden del LRU)."""
        marker = self.paths(asset_id).root / ACCESS_MARKER
        if not marker.parent.exists():
            return
        marker.write_text(str(when if when is not None else time.time()))

    def last_access(self, asset_id: str) -> float:
        """Momento del ultimo uso. 0.0 si nunca se marco."""
        marker = self.paths(asset_id).root / ACCESS_MARKER
        try:
            return float(marker.read_text().strip())
        except (FileNotFoundError, ValueError, OSError):
            return 0.0

    def size_bytes(self, asset_id: str) -> int:
        return _tree_size(self.paths(asset_id).root)

    def total_bytes(self) -> int:
        return _tree_size(self._root)

    def asset_ids(self) -> list[str]:
        if not self._root.exists():
            return []
        return sorted(d.name for d in self._root.iterdir() if d.is_dir())

    # --- Limpieza -----------------------------------------------------------

    def remove(self, asset_id: str) -> bool:
        """Borra el asset entero. False si el sistema no dejo."""
        root = self.paths(asset_id).root
        try:
            shutil.rmtree(root)
        except FileNotFoundError:
            return True
        except OSError as exc:
            # En Windows un segmento abierto por FFmpeg no se puede borrar.
            log.warning("no se pudo borrar el asset", asset_id=asset_id, error=str(exc))
            return False
        log.info("asset borrado", asset_id=asset_id)
        return True

    def collect(self, max_bytes: int, *, keep: set[str] | None = None) -> list[str]:
        """Borra los assets menos usados hasta bajar de `max_bytes`.

        `keep` protege los que tienen sesiones activas. Si lo unico que queda
        esta protegido, se detiene: es preferible pasarse del tope a cortarle
        la reproduccion a alguien.

        Los assets pineados tampoco se tocan, y el chequeo va aca adentro y no
        en `keep` porque son tres los que llaman a este metodo y olvidarselo en
        uno solo alcanza para perder una codificacion de seis horas.
        """
        protected = keep or set()
        removed: list[str] = []

        candidates = sorted(
            (
                a
                for a in self.asset_ids()
                if a not in protected and not self.is_pinned(a)
            ),
            key=self.last_access,
        )

        total = self.total_bytes()
        for asset_id in candidates:
            if total <= max_bytes:
                break
            size = self.size_bytes(asset_id)
            if self.remove(asset_id):
                total -= size
                removed.append(asset_id)

        if removed:
            log.info("gc completado", removed=len(removed), total_bytes=total)
        return removed

    def clear(self) -> None:
        """Vacia el cache sin borrar el directorio raiz (puede ser un mount)."""
        if not self._root.exists():
            self._root.mkdir(parents=True, exist_ok=True)
            return
        for entry in list(self._root.iterdir()):
            if entry.is_dir() and not entry.is_symlink():
                shutil.rmtree(entry, ignore_errors=True)
            else:
                entry.unlink(missing_ok=True)


def _tree_size(root: Path) -> int:
    """Bytes ocupados por un arbol de directorios."""
    if not root.exists():
        return 0
    total = 0
    for path in root.rglob("*"):
        if path.is_file():
            try:
                total += path.stat().st_size
            except OSError:
                continue
    return total
