"""Endpoints REST y servido de playlists HLS.

Las playlists se calculan en cada request a partir de las duraciones reales que
FFmpeg fue escribiendo. Los segmentos, en cambio, son archivos: los sirve
`StaticFiles` montado sobre el cache.
"""

from __future__ import annotations

from pathlib import Path

import structlog
from fastapi import APIRouter, Query, Request, Response

from app.config import Settings, get_settings
from app.errors import ApiError
from app.models.media import Media
from app.models.schemas import (
    AudioTrackSchema,
    MediaCreate,
    MediaKind,
    MediaPatch,
    MediaResponse,
    ProgressUpdate,
    StreamRequest,
    StreamResponse,
    SubtitleTrackSchema,
)
from app.services.asset_builder import Asset, AssetBuilder
from app.services.asset_store import AssetStore, asset_id_for
from app.services.media_service import MediaService
from app.services.session_manager import Session, SessionManager

log = structlog.get_logger("api")

router = APIRouter()
hls_router = APIRouter()

ALLOWED_EXTENSIONS = frozenset({".mp4", ".mkv"})
PLAYLIST_MEDIA_TYPE = "application/vnd.apple.mpegurl"


def _builder(request: Request) -> AssetBuilder:
    return request.app.state.builder


def _sessions(request: Request) -> SessionManager:
    return request.app.state.sessions


def _store(request: Request) -> AssetStore:
    return request.app.state.store


def _media_service(request: Request) -> MediaService:
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


def _validate_path(file_path: str, media_root: Path | None) -> Path:
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


def _resolve_media_path(file_path: str, media_root: Path) -> Path:
    """Convierte la ruta relativa que manda el formulario en absoluta validada.

    Rechazar la absoluta no es cosmetico: `Path("/media") / "/etc/passwd"` da
    `/etc/passwd`, porque un operando absoluto a la derecha reemplaza al de la
    izquierda en vez de concatenarse. Sin este chequeo el `MEDIA_ROOT` se
    evapora y el join deja de contener nada.

    El `..` no necesita chequeo aparte: `_validate_path` resuelve la ruta y lo
    caza la validacion de pertenencia a `MEDIA_ROOT`.
    """
    candidate = Path(file_path)
    if candidate.is_absolute():
        raise ApiError(
            400, "invalid_path", "La ruta debe ser relativa a MEDIA_ROOT",
        )

    return _validate_path(str(media_root / candidate), media_root)


def _media_response(media: Media) -> MediaResponse:
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


def _stream_response(asset: Asset, session: Session) -> StreamResponse:
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


def _guard_capacity(
    request: Request, source: Path, settings: Settings,
) -> None:
    """Rechaza aperturas nuevas si no hay lugar o no hay espacio."""
    store = _store(request)
    builder = _builder(request)
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
        keep=_sessions(request).referenced_asset_ids() | builder.building_asset_ids(),
    )
    if store.total_bytes() >= settings.MAX_CACHE_SIZE:
        raise ApiError(
            507,
            "storage_limit",
            "El cache esta lleno y no hay nada que liberar. Cerrar sesiones activas.",
        )


# --- API --------------------------------------------------------------------

@router.post("/stream", status_code=201)
async def create_stream(body: StreamRequest, request: Request) -> StreamResponse:
    """Abre un archivo y devuelve la sesion con el master playlist."""
    settings = get_settings()
    source = _validate_path(body.file_path, settings.MEDIA_ROOT)

    _guard_capacity(request, source, settings)

    asset = await _builder(request).open(source)
    session = _sessions(request).create(asset.id)

    log.info("stream abierto", asset_id=asset.id, session_id=session.id)
    return _stream_response(asset, session)


@router.get("/sessions/{session_id}")
async def get_session(session_id: str, request: Request) -> StreamResponse:
    """Estado del build. El player lo consulta para la barra de progreso."""
    session = _sessions(request).get(session_id)
    if session is None:
        raise ApiError(404, "session_not_found", f"La sesion {session_id} no existe")

    asset = _builder(request).get(session.asset_id)
    if asset is None:
        raise ApiError(
            404, "asset_not_found", "El asset de la sesion ya no esta disponible",
        )

    return _stream_response(asset, session)


@router.post("/heartbeat/{session_id}", status_code=204)
async def heartbeat(session_id: str, request: Request) -> Response:
    """Mantiene viva la sesion. El player lo llama cada 30 s."""
    sessions = _sessions(request)
    if not sessions.heartbeat(session_id):
        raise ApiError(404, "session_not_found", f"La sesion {session_id} no existe")

    # Cada latido corre al asset al frente del LRU: mientras alguien lo mire,
    # el GC no lo elige.
    _store(request).touch(sessions.get(session_id).asset_id)
    return Response(status_code=204)


# --- Galeria / ABM de medios ------------------------------------------------
#
# Solo las firmas: la BD todavia no existe, asi que cada handler contesta 501
# con el formato de error de siempre. La validacion que si esta resuelta
# (rutas, paginacion, campos editables) ya se aplica: es la misma que usa el
# resto de la API y no hay razon para escribirla dos veces.


def _todo(what: str) -> None:
    raise ApiError(501, "not_implemented", f"{what}: pendiente de implementacion")


@router.post("/media", status_code=201)
async def create_media(
    body: MediaCreate, request: Request, response: Response,
) -> MediaResponse:
    """Alta de una pelicula o episodio a partir de su ruta relativa.

    No dispara ninguna codificacion: registrar y reproducir son dos acciones, y
    la segunda la resuelve `POST /stream` con el `asset_id` que queda en la
    ficha.
    """
    service = _media_service(request)
    source = _resolve_media_path(body.file_path, get_settings().MEDIA_ROOT)

    # Chequear antes de analizar: ffprobe sobre un montaje de red cuesta
    # segundos y el archivo ya esta registrado. El UNIQUE sobre `path_key` es
    # la red de abajo.
    existing = service.find(source)
    if existing is not None:
        raise ApiError(
            409,
            "media_already_exists",
            f"Ya existe una ficha para {existing.file_path} "
            f"(id {existing.id_media})",
        )

    media = await service.register(source)
    response.headers["Location"] = f"/api/v1/media/{media.id_media}"
    return _media_response(media)


@router.get("/media")
async def list_media(
    q: str | None = None,
    kind: MediaKind | None = None,
    in_list: bool | None = None,
    unfinished: bool = False,
    sort: str = Query("title", pattern="^(title|added|progress)$"),
    limit: int = Query(10, ge=1, le=100),
    offset: int = Query(0, ge=0),
) -> dict:
    """Listado paginado. La galeria lo llama una vez por seccion.

    Cada seccion del mockup es una combinacion de estos filtros: "My list" es
    `in_list=true`, "Continue watching" es `unfinished=true&sort=progress`,
    "Series" es `kind=series`. El `total` de la respuesta es el contador que
    va en el encabezado de cada seccion.
    """
    _todo("El listado de medios")


@router.get("/media/{id_media}")
async def get_media(id_media: str) -> dict:
    """Detalle de una ficha: metadatos, pistas, editoriales y progreso."""
    _todo("El detalle de un medio")


@router.patch("/media/{id_media}")
async def update_media(id_media: str, body: MediaPatch) -> dict:
    """Edita los campos editoriales. Devuelve la ficha completa."""
    _todo("La edicion de un medio")


@router.delete("/media/{id_media}", status_code=204)
async def delete_media(id_media: str, purge: bool = False) -> Response:
    """Baja de la ficha. Con `purge` borra tambien el cache HLS del asset."""
    _todo("La baja de un medio")


@router.post("/media/{id_media}/refresh")
async def refresh_media(id_media: str) -> dict:
    """Re-analiza el archivo y pisa los campos derivados, no los editoriales."""
    _todo("El re-analisis de un medio")


@router.put("/media/{id_media}/progress")
async def set_progress(id_media: str, body: ProgressUpdate) -> dict:
    """Guarda donde quedo el espectador. Alimenta "Continue watching"."""
    _todo("El progreso de reproduccion")


# --- Playlists --------------------------------------------------------------

def _playlist_response(
    request: Request, content: str | None, asset_id: str,
) -> Response:
    """Sirve la playlist, distinguiendo "no existe" de "todavia no".

    Un asset en construccion cuya playlist aun no se escribio no es un 404: eso
    le dice al cliente que deje de pedirla, y hls.js efectivamente abandona
    despues de unos reintentos.
    """
    if content is not None:
        return Response(content=content, media_type=PLAYLIST_MEDIA_TYPE)

    if _builder(request).get(asset_id) is None:
        raise ApiError(404, "asset_not_found", f"El asset {asset_id} no existe")

    raise ApiError(
        503,
        "playlist_not_ready",
        f"El asset {asset_id} todavia se esta generando. Reintentar en unos segundos.",
        headers={"Retry-After": "2"},
    )


@hls_router.get("/hls/{asset_id}/master.m3u8")
async def master_playlist(asset_id: str, request: Request) -> Response:
    return _playlist_response(
        request, _builder(request).master_playlist(asset_id), asset_id
    )


@hls_router.get("/hls/{asset_id}/video/playlist.m3u8")
async def video_playlist(asset_id: str, request: Request) -> Response:
    return _playlist_response(
        request, _builder(request).media_playlist(asset_id), asset_id
    )


@hls_router.get("/hls/{asset_id}/audio/{track}/playlist.m3u8")
async def audio_playlist(asset_id: str, track: int, request: Request) -> Response:
    builder = _builder(request)

    # Una pista que no existe es un 404 de verdad, no un "todavia no".
    asset = builder.get(asset_id)
    if asset is not None and track not in asset.audio:
        raise ApiError(
            404, "track_not_found", f"El asset {asset_id} no tiene la pista {track}",
        )

    return _playlist_response(
        request, builder.media_playlist(asset_id, track=track), asset_id
    )
