"""Entry point de la aplicacion FastAPI."""

from __future__ import annotations

import asyncio
import mimetypes
from contextlib import asynccontextmanager

import structlog
from fastapi import FastAPI, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from fastapi.staticfiles import StaticFiles
from starlette.middleware.base import BaseHTTPMiddleware
from starlette.responses import Response

from app.api.routes import hls_router, router
from app.config import get_settings
from app.errors import ApiError
from app.log import setup as setup_logging
from app.services.asset_builder import AssetBuilder
from app.services.asset_store import AssetStore
from app.services.session_manager import SessionManager
from app.services.transcoder import TranscodeOptions

log = structlog.get_logger("main")

mimetypes.add_type("text/vtt", ".vtt")
mimetypes.add_type("application/vnd.apple.mpegurl", ".m3u8")
mimetypes.add_type("video/iso.segment", ".m4s")

# Los segmentos y el init nunca cambian una vez escritos. Las playlists se
# calculan en cada request y crecen mientras el build corre.
IMMUTABLE_SUFFIXES = (".m4s", ".mp4", ".ts")
VOLATILE_SUFFIXES = (".m3u8", ".vtt")


class CacheControlMiddleware(BaseHTTPMiddleware):
    """Cache inmutable para los segmentos, no-store para playlists y subs."""

    async def dispatch(self, request: Request, call_next) -> Response:
        response = await call_next(request)
        path = request.url.path
        if path.endswith(IMMUTABLE_SUFFIXES):
            response.headers["Cache-Control"] = "public, max-age=31536000, immutable"
        elif path.endswith(VOLATILE_SUFFIXES):
            response.headers["Cache-Control"] = "no-store"
        return response


async def cleanup_loop(app: FastAPI) -> None:
    """Poda sesiones muertas y mantiene el cache bajo el tope.

    Sin esto se acumulan sesiones fantasma y el disco se llena: es el unico
    mecanismo que libera espacio.
    """
    settings = get_settings()
    store: AssetStore = app.state.store
    builder: AssetBuilder = app.state.builder
    sessions: SessionManager = app.state.sessions

    while True:
        await asyncio.sleep(settings.CLEANUP_INTERVAL)
        try:
            sessions.prune()
            removed = store.collect(
                settings.MAX_CACHE_SIZE,
                keep=sessions.referenced_asset_ids() | builder.building_asset_ids(),
            )
            for asset_id in removed:
                builder.forget(asset_id)
        except asyncio.CancelledError:
            raise
        except Exception as exc:  # el loop no puede morirse por un error suelto
            log.error("error en el ciclo de limpieza", error=str(exc))


@asynccontextmanager
async def lifespan(app: FastAPI):
    settings = get_settings()
    setup_logging(debug=False)

    cache_dir = settings.CACHE_DIR.resolve()
    cache_dir.mkdir(parents=True, exist_ok=True)

    store = AssetStore(root=cache_dir)
    app.state.store = store
    app.state.sessions = SessionManager(
        inactive_after=settings.SESSION_INACTIVE_AFTER,
        expire_after=settings.SESSION_EXPIRE_AFTER,
    )
    app.state.builder = AssetBuilder(
        store=store,
        options=TranscodeOptions(
            hls_time=settings.HLS_TIME,
            crf=settings.FFMPEG_CRF,
            preset=settings.FFMPEG_PRESET,
            audio_bitrate=settings.AUDIO_BITRATE,
            audio_channels=settings.AUDIO_CHANNELS,
        ),
        max_concurrent=settings.MAX_CONCURRENT_FFMPEG,
    )

    # El cache persiste entre arranques a proposito: reabrir una pelicula ya
    # procesada no deberia costar nada. Lo unico que se hace al arrancar es
    # respetar el tope de tamano.
    store.collect(settings.MAX_CACHE_SIZE)

    # Las rutas de playlist se registran antes del mount: Starlette resuelve en
    # orden de registro, y si el mount ganara serviria el internal.m3u8 de
    # FFmpeg en vez de la playlist calculada.
    app.mount("/hls", StaticFiles(directory=str(cache_dir)), name="hls")
    app.mount("/static", StaticFiles(directory="static"), name="static")

    cleanup = asyncio.create_task(cleanup_loop(app))

    log.info("servidor iniciado", cache_dir=str(cache_dir), port=settings.PORT)
    yield

    cleanup.cancel()
    await app.state.builder.shutdown()
    log.info("servidor detenido")


app = FastAPI(title="Stream Media", lifespan=lifespan)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
    expose_headers=["Content-Length", "Content-Range"],
)
app.add_middleware(CacheControlMiddleware)


@app.exception_handler(ApiError)
async def api_error_handler(request: Request, exc: ApiError) -> JSONResponse:
    """Formato de error unico: {"error": slug, "detail": texto}."""
    return JSONResponse(
        status_code=exc.status_code,
        content={"error": exc.error, "detail": exc.detail},
        headers=exc.headers,
    )


app.include_router(router, prefix="/api/v1")
app.include_router(hls_router)
