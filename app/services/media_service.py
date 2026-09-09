"""Casos de uso del ABM de medios.

Es el lugar donde va a vivir la orquestacion del alta (validar la ruta, correr
`analyze`, calcular el `asset_id`, deduplicar, persistir) y del borrado con
purga del cache HLS. Hoy casi todo es delegacion directa al repositorio, a
proposito: tener la capa desde el principio hace que esa logica tenga donde
caer en vez de terminar dentro de un handler, y que leer `routes.py` no obligue
a saber SQL.

Es el equivalente de `asset_builder` para el catalogo: el que habla con varios
colaboradores para que los endpoints no tengan que hacerlo.
"""

from __future__ import annotations

from pathlib import Path

import structlog

from app.errors import ApiError, FFmpegError
from app.models.media import Media
from app.repository.media_repository import MediaRepository
from app.services.asset_store import asset_id_for
from app.services.media_analyzer import SourceInfo, analyze

log = structlog.get_logger("media_service")


class MediaService:
    """Operaciones del catalogo, en el vocabulario del ABM.

    Recibe y devuelve rutas **absolutas** — es lo que produce `validate_path` y
    lo que necesitan ffprobe y FFmpeg — y guarda las relativas a `media_root`.
    La traduccion pasa solo por aca: ni los handlers ni el repositorio la hacen.
    """

    def __init__(self, repository: MediaRepository, media_root: Path) -> None:
        self._repository = repository
        # El ABM no funciona sin MEDIA_ROOT: es el ancla de las rutas guardadas.
        # En docker siempre esta seteado (SM_MEDIA_ROOT en docker-compose).
        self._media_root = media_root

    async def register(self, source: Path) -> Media:
        """Alta: analiza el archivo y persiste la ficha.

        `source` es la ruta absoluta ya validada por `validate_path`. Correr
        ffprobe es lo que prueba que el archivo sea un medio de video de verdad
        — la extension no prueba nada —, asi que el alta falla aca si no lo es.

        No dispara ninguna codificacion: dar de alta y reproducir son dos
        acciones distintas, y la segunda la resuelve `POST /stream`.
        """
        relative = source.relative_to(self._media_root)
        info = await self._analyze(source)
        media = Media.from_source(relative, info, asset_id=asset_id_for(source))

        log.info(
            "alta de medio",
            id_media=media.id_media,
            file_path=media.file_path,
            strategy=info.strategy.value,
        )
        return self._repository.add(media)

    async def _analyze(self, source: Path) -> SourceInfo:
        """`analyze` con sus dos fallos traducidos a un error de la API.

        Un `.mkv` de cero bytes pasa las cinco validaciones de ruta: la unica
        prueba de que el archivo es un medio de video es que ffprobe encuentre
        un stream de video. Sus dos formas de fallar — el binario termina mal, o
        termina bien pero no hay video — son el mismo `400` para quien carga la
        pelicula, y sin esto salen como un `500` generico.
        """
        try:
            return await analyze(source)
        except FFmpegError as exc:
            log.warning(
                "ffprobe fallo en el alta",
                path=str(source),
                returncode=exc.returncode,
            )
            raise ApiError(
                400,
                "invalid_media",
                f"No se pudo leer el archivo: {source.name}",
            ) from exc
        except ValueError as exc:
            log.warning("archivo sin video en el alta", path=str(source))
            raise ApiError(
                400,
                "invalid_media",
                f"El archivo no es un medio de video valido: {source.name}",
            ) from exc

    def add(self, media: Media) -> Media:
        return self._repository.add(media)

    def get(self, id_media: str) -> Media | None:
        return self._repository.get(id_media)

    def find(self, source: Path) -> Media | None:
        """Ficha de un archivo por su ruta absoluta, o None.

        Es el chequeo de duplicados del alta.
        """
        return self._repository.by_path(source.relative_to(self._media_root))

    def update(self, id_media: str, **fields) -> Media | None:
        return self._repository.update(id_media, **fields)

    def set_asset(self, id_media: str, asset_id: str) -> None:
        self._repository.set_asset(id_media, asset_id)

    def delete(self, id_media: str) -> bool:
        return self._repository.delete(id_media)

    def page(
        self,
        *,
        q: str | None = None,
        kind: str | None = None,
        in_list: bool | None = None,
        sort: str = "title",
        limit: int = 10,
        offset: int = 0,
    ) -> tuple[list[Media], int]:
        """Una pagina del listado y el total con los mismos filtros.

        Los dos datos siempre viajan juntos — el total es el "6 titles" del
        encabezado de cada seccion de la galeria —, y que los filtros se pasen
        una sola vez evita que la lista y el contador se desincronicen.
        """
        filters = {"q": q, "kind": kind, "in_list": in_list}
        items = self._repository.list(**filters, sort=sort, limit=limit, offset=offset)
        return items, self._repository.count(**filters)
