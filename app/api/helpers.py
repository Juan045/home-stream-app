"""Auxiliares de los endpoints: estado, validacion de rutas y armado de respuestas.

Todo lo que `routes.py` necesita pero que no es una ruta vive aca, para que ese
archivo se lea como lo que es: la lista de endpoints de la API.

Nada de esto decide *que* hace un endpoint — eso sigue en el handler o, cuando
hay orquestacion, en el service. Aca solo estan las piezas que varios handlers
comparten.
"""

from __future__ import annotations

from pathlib import Path

from fastapi import Request, Response

from app.config import Settings
from app.errors import ApiError
from app.models.media import Media
from app.models.schemas import (
    AudioTrackSchema,
    MediaListItem,
    MediaResponse,
    StreamResponse,
    SubtitleTrackSchema,
)
from app.services.asset_builder import Asset, AssetBuilder
from app.services.asset_store import AssetStore, asset_id_for
from app.services.media_service import MediaService
from app.services.session_manager import Session, SessionManager

ALLOWED_EXTENSIONS = frozenset({".mp4", ".mkv"})
PLAYLIST_MEDIA_TYPE = "application/vnd.apple.mpegurl"


# --- Estado de la aplicacion ------------------------------------------------

def get_builder(request: Request) -> AssetBuilder:
    return request.app.state.builder


def get_sessions(request: Request) -> SessionManager:
    return request.app.state.sessions


def get_store(request: Request) -> AssetStore:
    return request.app.state.store


def get_media_service(request: Request) -> MediaService:
    """El ABM, o un error claro si el servidor no esta configurado para tenerlo.

    Sin `MEDIA_ROOT` no hay ancla contra la cual resolver las rutas relativas
    que recibe el alta, y tampoco frontera que las contenga: el catalogo no se
    puede levantar a medias.
    """
    service = getattr(request.app.state, "media", None)
    if service is None:
        raise ApiError(
            500,
            "media_root_not_configured",
            "SM_MEDIA_ROOT no esta configurado: el ABM no puede resolver rutas",
        )
    return service


# --- Rutas de archivo -------------------------------------------------------

def validate_path(file_path: str, media_root: Path | None) -> Path:
    """Valida la ruta del archivo segun las reglas de seguridad."""
    path = Path(file_path)

    if not path.is_absolute():
        raise ApiError(400, "invalid_path", "La ruta debe ser absoluta")

    resolved = path.resolve()
    if ".." in resolved.parts:
        raise ApiError(400, "path_traversal", "Path traversal detectado")

    if resolved.suffix.lower() not in ALLOWED_EXTENSIONS:
        raise ApiError(
            400,
            "unsupported_extension",
            f"Extension no soportada: {resolved.suffix}. "
            f"Permitidas: {', '.join(sorted(ALLOWED_EXTENSIONS))}",
        )

    if not resolved.exists():
        raise ApiError(404, "file_not_found", f"El archivo no existe: {resolved}")

    if media_root is not None and not resolved.is_relative_to(media_root.resolve()):
        raise ApiError(
            400,
            "outside_media_root",
            f"El archivo esta fuera del directorio permitido: {media_root}",
        )

    return resolved


def resolve_media_path(file_path: str, media_root: Path) -> Path:
    """Convierte la ruta relativa que manda el formulario en absoluta validada.

    Rechazar la absoluta no es cosmetico: `Path("/media") / "/etc/passwd"` da
    `/etc/passwd`, porque un operando absoluto a la derecha reemplaza al de la
    izquierda en vez de concatenarse. Sin este chequeo el `MEDIA_ROOT` se
    evapora y el join deja de contener nada.

    El `..` no necesita chequeo aparte: `validate_path` resuelve la ruta y lo
    caza la validacion de pertenencia a `MEDIA_ROOT`.
    """
    candidate = Path(file_path)
    if candidate.is_absolute():
        raise ApiError(
            400, "invalid_path", "La ruta debe ser relativa a MEDIA_ROOT",
        )

    return validate_path(str(media_root / candidate), media_root)


# --- Capacidad --------------------------------------------------------------

def guard_capacity(request: Request, source: Path, settings: Settings) -> None:
    """Rechaza aperturas nuevas si no hay lugar o no hay espacio."""
    store = get_store(request)
    builder = get_builder(request)
    asset_id = asset_id_for(source)

    # Un asset ya abierto o ya cacheado no genera trabajo nuevo: siempre pasa.
    if builder.get(asset_id) is not None or store.exists(asset_id):
        return

    if builder.building_count() >= settings.MAX_CONCURRENT_FFMPEG:
        raise ApiError(
            503,
            "too_many_jobs",
            "Hay demasiados procesos activos. Reintentar en unos segundos.",
        )

    store.collect(
        settings.MAX_CACHE_SIZE,
        keep=get_sessions(request).referenced_asset_ids()
        | builder.building_asset_ids(),
    )
    if store.total_bytes() >= settings.MAX_CACHE_SIZE:
        raise ApiError(
            507,
            "storage_limit",
            "El cache esta lleno y no hay nada que liberar. Cerrar sesiones activas.",
        )


# --- Armado de respuestas ---------------------------------------------------

def media_or_404(media: Media | None, id_media: str) -> Media:
    """Corta con un 404 si la ficha no existe.

    Lo comparten todos los endpoints que reciben un `id_media`: el slug y el
    mensaje tienen que ser el mismo en los cinco.
    """
    if media is None:
        raise ApiError(
            404, "media_not_found", f"No existe una ficha con id {id_media}",
        )
    return media


def media_response(media: Media) -> MediaResponse:
    """Aplana la ficha: los derivados suben al nivel de arriba."""
    info = media.source_info()
    return MediaResponse(
        id_media=media.id_media,
        file_path=media.file_path,
        file_name=media.file_name,
        asset_id=media.asset_id,
        title=media.title,
        kind=media.kind,
        year=media.year,
        synopsis=media.synopsis,
        genres=media.genres,
        notes=media.notes,
        in_list=media.in_list,
        duration=media.duration,
        video_codec=info.video_codec,
        width=info.width,
        height=info.height,
        strategy=info.strategy.value,
        audio_tracks=[
            AudioTrackSchema(
                index=track.index,
                codec=track.codec,
                channels=track.channels,
                language=track.language,
                title=track.title,
            )
            for track in info.audio_tracks
        ],
        subtitle_tracks=[
            SubtitleTrackSchema(
                index=track.index,
                codec=track.codec,
                language=track.language,
                title=track.title,
            )
            for track in info.subtitle_tracks
        ],
        created_at=media.created_at,
        updated_at=media.updated_at,
    )


def media_list_item(media: Media) -> MediaListItem:
    """Recorta la ficha a lo que dibuja una tarjeta de la grilla."""
    return MediaListItem(
        id_media=media.id_media,
        title=media.title,
        kind=media.kind,
        year=media.year,
        duration=media.duration,
    )


def stream_response(asset: Asset, session: Session) -> StreamResponse:
    return StreamResponse(
        session_id=session.id,
        asset_id=asset.id,
        status=asset.status,
        playable=asset.playable,
        master_url=f"/hls/{asset.id}/master.m3u8",
        duration_seconds=asset.info.duration,
        progress=asset.progress,
        strategy=asset.info.strategy.value,
        audio_tracks=[
            AudioTrackSchema(
                index=track.index,
                codec=track.codec,
                channels=track.channels,
                language=track.language,
                title=track.title,
            )
            for track in asset.info.audio_tracks
        ],
        subtitle_tracks=[
            SubtitleTrackSchema(
                index=track.index,
                codec=track.codec,
                language=track.language,
                title=track.title,
                url=_subtitle_url(asset, track.index),
            )
            for track in asset.info.subtitle_tracks
        ],
        error=asset.error,
    )


def _subtitle_url(asset: Asset, index: int) -> str | None:
    """URL del .vtt, o None si todavia no se extrajo o fallo."""
    name = asset.ready_subtitles().get(index)
    return f"/hls/{asset.id}/subs/{name}" if name else None


def playlist_response(
    request: Request, content: str | None, asset_id: str,
) -> Response:
    """Sirve la playlist, distinguiendo "no existe" de "todavia no".

    Un asset en construccion cuya playlist aun no se escribio no es un 404: eso
    le dice al cliente que deje de pedirla, y hls.js efectivamente abandona
    despues de unos reintentos.
    """
    if content is not None:
        return Response(content=content, media_type=PLAYLIST_MEDIA_TYPE)

    if get_builder(request).get(asset_id) is None:
        raise ApiError(404, "asset_not_found", f"El asset {asset_id} no existe")

    raise ApiError(
        503,
        "playlist_not_ready",
        f"El asset {asset_id} todavia se esta generando. Reintentar en unos segundos.",
        headers={"Retry-After": "2"},
    )


def not_implemented(what: str) -> None:
    """Marca un endpoint del ABM que todavia no tiene logica detras."""
    raise ApiError(501, "not_implemented", f"{what}: pendiente de implementacion")
