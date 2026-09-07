"""Ficha de una pelicula en el ABM: la entidad, sin SQL.

Una fila por archivo. Los datos *derivados* del archivo (codecs, pistas,
resolucion) viven serializados en `info`, tal cual los devuelve
`media_analyzer`: la ficha no los interpreta y el dia que `SourceInfo` gane un
campo no hay que migrar la tabla. Los *editoriales* (titulo, ano, notas) son
columnas porque el listado filtra y ordena por ellas.

`file_path` se guarda **relativa a `MEDIA_ROOT`** ("films/x.mkv"), no absoluta:
el punto de montaje es lo unico que cambia si algun dia esto corre fuera del
contenedor, y asi el catalogo no queda apuntando a un `/media` que no existe.
La absoluta que necesitan ffprobe y FFmpeg se deriva con `absolute_path`.

Las consultas estan en `repository.media_repository`.
"""

from __future__ import annotations

import os
from dataclasses import dataclass, field
from pathlib import Path
from uuid import uuid4

from app.services.media_analyzer import SourceInfo, from_dict, to_dict


def path_key_for(file_path: str | Path) -> str:
    """Clave de deduplicacion sobre la ruta relativa.

    Sin `normcase`, `Films/Peli.mkv` y `films/peli.mkv` entrarian como dos
    fichas distintas. No lleva `resolve()`: la ruta es relativa y resolverla la
    pegaria contra el directorio de trabajo del proceso.
    """
    return os.path.normcase(str(Path(file_path)))


@dataclass
class Media:
    """Una ficha del catalogo."""

    id_media: str
    file_path: str      # relativa a MEDIA_ROOT
    file_name: str
    title: str
    path_key: str = ""
    asset_id: str | None = None
    duration: float = 0.0
    info: dict = field(default_factory=dict)
    kind: str = "film"
    year: int | None = None
    synopsis: str | None = None
    genres: list[str] = field(default_factory=list)
    notes: str | None = None
    in_list: bool = False
    created_at: str = ""
    updated_at: str = ""

    def __post_init__(self) -> None:
        if not self.path_key:
            self.path_key = path_key_for(self.file_path)

    @classmethod
    def from_source(
        cls, relative: Path, info: SourceInfo, asset_id: str | None = None
    ) -> Media:
        """Arma la ficha a partir del analisis del archivo.

        `relative` es la ruta relativa a `MEDIA_ROOT`. El titulo arranca siendo
        el nombre del archivo sin extension; el usuario lo corrige despues con
        el PATCH.
        """
        return cls(
            id_media=uuid4().hex,
            file_path=str(relative),
            file_name=relative.name,
            title=relative.stem,
            asset_id=asset_id,
            duration=info.duration,
            info=to_dict(info),
        )

    def absolute_path(self, media_root: Path) -> Path:
        """La ruta real en disco. Es lo que reciben ffprobe y FFmpeg."""
        return media_root / self.file_path

    def source_info(self) -> SourceInfo:
        """Rehidrata el `SourceInfo` guardado, sin volver a correr ffprobe."""
        return from_dict(self.info)
