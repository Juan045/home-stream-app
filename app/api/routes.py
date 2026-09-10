"""Endpoints REST y servido de playlists HLS.

Solo las rutas: la validacion, el acceso al estado y el armado de las respuestas
estan en `helpers.py`.

Las playlists se calculan en cada request a partir de las duraciones reales que
FFmpeg fue escribiendo. Los segmentos, en cambio, son archivos: los sirve
`StaticFiles` montado sobre el cache.
"""

from __future__ import annotations

import structlog
from fastapi import APIRouter, Query, Request, Response

from app.api.helpers import (
    get_builder,
    get_media_service,
    get_sessions,
    get_store,
    guard_capacity,
    media_list_item,
    media_or_404,
    media_response,
    not_implemented,
    playlist_response,
    resolve_media_path,
    stream_response,
    validate_path,
)
from app.config import get_settings
from app.errors import ApiError
from app.models.schemas import (
    MediaCreate,
    MediaKind,
    MediaListResponse,
    MediaPatch,
    MediaResponse,
    ProgressUpdate,
    StreamRequest,
    StreamResponse,
)

log = structlog.get_logger("api")

router = APIRouter()
hls_router = APIRouter()


# --- API --------------------------------------------------------------------

@router.post("/stream", status_code=201)
async def create_stream(body: StreamRequest, request: Request) -> StreamResponse:
    """Abre un archivo y devuelve la sesion con el master playlist."""
    settings = get_settings()
    source = validate_path(body.file_path, settings.MEDIA_ROOT)

    guard_capacity(request, source, settings)

    asset = await get_builder(request).open(source)
    session = get_sessions(request).create(asset.id)

    log.info("stream abierto", asset_id=asset.id, session_id=session.id)
    return stream_response(asset, session)


@router.get("/sessions/{session_id}")
async def get_session(session_id: str, request: Request) -> StreamResponse:
    """Estado del build. El player lo consulta para la barra de progreso."""
    session = get_sessions(request).get(session_id)
    if session is None:
        raise ApiError(404, "session_not_found", f"La sesion {session_id} no existe")

    asset = get_builder(request).get(session.asset_id)
    if asset is None:
        raise ApiError(
            404, "asset_not_found", "El asset de la sesion ya no esta disponible",
        )

    return stream_response(asset, session)


@router.post("/heartbeat/{session_id}", status_code=204)
async def heartbeat(session_id: str, request: Request) -> Response:
    """Mantiene viva la sesion. El player lo llama cada 30 s."""
    sessions = get_sessions(request)
    if not sessions.heartbeat(session_id):
        raise ApiError(404, "session_not_found", f"La sesion {session_id} no existe")

    # Cada latido corre al asset al frente del LRU: mientras alguien lo mire,
    # el GC no lo elige.
    get_store(request).touch(sessions.get(session_id).asset_id)
    return Response(status_code=204)


# --- Galeria / ABM de medios ------------------------------------------------
#
# De estos, solo el alta esta conectada. El resto son firmas: contestan 501 con
# el formato de error de siempre, pero la validacion que ya esta resuelta
# (paginacion, campos editables) se aplica igual.

@router.post("/media", status_code=201)
async def create_media(
    body: MediaCreate, request: Request, response: Response,
) -> MediaResponse:
    """Alta de una pelicula o episodio a partir de su ruta relativa.

    No dispara ninguna codificacion: registrar y reproducir son dos acciones, y
    la segunda la resuelve `POST /stream` con el `asset_id` que queda en la
    ficha.
    """
    service = get_media_service(request)
    source = resolve_media_path(body.file_path, get_settings().MEDIA_ROOT)

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
    return media_response(media)


@router.get("/media")
async def list_media(
    request: Request,
    q: str | None = None,
    kind: MediaKind | None = None,
    in_list: bool | None = None,
    sort: str = Query("title", pattern="^(title|added)$"),
    limit: int = Query(10, ge=1, le=100),
    offset: int = Query(0, ge=0),
) -> MediaListResponse:
    """Listado paginado. La galeria lo llama una vez por seccion.

    Cada seccion es una combinacion de estos filtros: "My list" es
    `in_list=true`, "Series" es `kind=series`, y el "31 titles" del pie es el
    `total` de una llamada sin filtros. Cada fila trae solo lo que dibuja una
    tarjeta; el panel de detalle lo llena `GET /media/{id_media}`.

    No toca el disco: listar diez fichas no puede costar diez `stat` sobre un
    montaje de red.

    Falta la seccion "Continue watching", que necesita el progreso de
    reproduccion y todavia no tiene tabla donde vivir.
    """
    items, total = get_media_service(request).page(
        q=q, kind=kind, in_list=in_list, sort=sort, limit=limit, offset=offset,
    )
    return MediaListResponse(
        items=[media_list_item(media) for media in items],
        total=total,
        limit=limit,
        offset=offset,
    )


@router.get("/media/{id_media}")
async def get_media(id_media: str, request: Request) -> MediaResponse:
    """Detalle de una ficha: metadatos, pistas y campos editoriales.

    Sale entero de la BD y no toca el disco: que el archivo siga estando se
    sabra al reproducirlo. Un `stat` por request sobre un montaje de red no es
    gratis, y que el disco este desmontado no invalida la ficha.
    """
    media = get_media_service(request).get(id_media)
    return media_response(media_or_404(media, id_media))


@router.patch("/media/{id_media}")
async def update_media(
    id_media: str, body: MediaPatch, request: Request,
) -> MediaResponse:
    """Edita los campos editoriales. Devuelve la ficha completa.

    `exclude_unset` es lo que separa "no lo toques" de "vacialo": lo que el
    usuario no mando no viaja, y un campo mandado en null si viaja y borra la
    columna. Sin eso, el primer submit parcial del formulario dejaria el resto
    de la ficha sin cambios pero tampoco habria forma de corregir un dato mal
    cargado.

    Los campos derivados del archivo no se pueden tocar: `MediaPatch` los
    rechaza con un 422 antes de llegar aca.
    """
    media = get_media_service(request).update(
        id_media, **body.model_dump(exclude_unset=True)
    )
    return media_response(media_or_404(media, id_media))


@router.delete("/media/{id_media}", status_code=204)
async def delete_media(id_media: str, purge: bool = False) -> Response:
    """Baja de la ficha. Con `purge` borra tambien el cache HLS del asset."""
    not_implemented("La baja de un medio")


@router.post("/media/{id_media}/refresh")
async def refresh_media(id_media: str) -> dict:
    """Re-analiza el archivo y pisa los campos derivados, no los editoriales."""
    not_implemented("El re-analisis de un medio")


@router.put("/media/{id_media}/progress")
async def set_progress(id_media: str, body: ProgressUpdate) -> dict:
    """Guarda donde quedo el espectador. Alimenta "Continue watching"."""
    not_implemented("El progreso de reproduccion")


# --- Playlists --------------------------------------------------------------

@hls_router.get("/hls/{asset_id}/master.m3u8")
async def master_playlist(asset_id: str, request: Request) -> Response:
    return playlist_response(
        request, get_builder(request).master_playlist(asset_id), asset_id
    )


@hls_router.get("/hls/{asset_id}/video/playlist.m3u8")
async def video_playlist(asset_id: str, request: Request) -> Response:
    return playlist_response(
        request, get_builder(request).media_playlist(asset_id), asset_id
    )


@hls_router.get("/hls/{asset_id}/audio/{track}/playlist.m3u8")
async def audio_playlist(asset_id: str, track: int, request: Request) -> Response:
    builder = get_builder(request)

    # Una pista que no existe es un 404 de verdad, no un "todavia no".
    asset = builder.get(asset_id)
    if asset is not None and track not in asset.audio:
        raise ApiError(
            404, "track_not_found", f"El asset {asset_id} no tiene la pista {track}",
        )

    return playlist_response(
        request, builder.media_playlist(asset_id, track=track), asset_id
    )
