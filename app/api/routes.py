"""Endpoints REST y servido de playlists HLS.

Las playlists se calculan en cada request a partir de las duraciones reales que
FFmpeg fue escribiendo. Los segmentos, en cambio, son archivos: los sirve
`StaticFiles` montado sobre el cache.
"""

from __future__ import annotations

from pathlib import Path

import structlog
from fastapi import APIRouter, Request, Response

from app.config import Settings, get_settings
from app.errors import ApiError
from app.models.schemas import (
    AudioTrackSchema,
    StreamRequest,
    StreamResponse,
    SubtitleTrackSchema,
)
from app.services.asset_builder import Asset, AssetBuilder
from app.services.asset_store import AssetStore, asset_id_for
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
