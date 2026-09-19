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
from app.manager.entityManager import connect
from app.repository.media_repository import MediaRepository
from app.services.asset_builder import AssetBuilder
from app.services.asset_store import AssetStore
from app.services.media_service import MediaService
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
            video_codec=settings.VIDEO_CODEC,
            crf=settings.FFMPEG_CRF,
            preset=settings.FFMPEG_PRESET,
            audio_bitrate=settings.AUDIO_BITRATE,
            audio_channels=settings.AUDIO_CHANNELS,
        ),
        max_concurrent=settings.MAX_CONCURRENT_FFMPEG,
    )

    # Catalogo del ABM. La conexion la abre el entityManager (con el esquema ya
    # cargado) y el repositorio solo la usa.
    app.state.db = connect(settings.DB_PATH.resolve())
    # MEDIA_ROOT resuelto: `validate_path` devuelve rutas resueltas y el
    # service hace `relative_to` contra esta. Si una viene sin resolver y la
    # otra no, el alta se cae.
    app.state.media = (
        MediaService(
            MediaRepository(app.state.db), media_root=settings.MEDIA_ROOT.resolve()
        )
        if settings.MEDIA_ROOT is not None
        else None
    )

    # El cache persiste entre arranques a proposito: reabrir una pelicula ya
    # procesada no deberia costar nada. Lo unico que se hace al arrancar es
    # respetar el tope de tamano.
    store.collect(settings.MAX_CACHE_SIZE)

    cleanup = asyncio.create_task(cleanup_loop(app))

    log.info("servidor iniciado", cache_dir=str(cache_dir), port=settings.PORT)
    yield

    cleanup.cancel()
    await app.state.builder.shutdown()
    app.state.db.close()
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


# --- Espacio de URLs --------------------------------------------------------
#
# Starlette resuelve en orden de registro y este bloque ES el orden: lo mas
# especifico arriba, el frontend ultimo. Dos consecuencias que no hay que
# reordenar:
#
#   - Las rutas de playlist van antes del mount de /hls. Si el mount ganara se
#     serviria el internal.m3u8 de FFmpeg en vez de la playlist calculada, y es
#     un fallo silencioso.
#   - El mount de "/" matchea todo, asi que va ultimo. Nada se registra despues,
#     ni aca ni en el lifespan.
#
# `check_dir=False` porque ni el cache ni el bundle existen necesariamente al
# importar: el cache lo crea el lifespan y el bundle lo escribe `npm run build`.
#
# `html=True` es lo que hace que "/" sirva index.html y "/new/" sirva
# new/index.html. No hay catch-all ni fallback: cada vista del frontend es un
# archivo en disco, porque Vite compila una pagina por ruta.

app.include_router(router, prefix="/api/v1")
app.include_router(hls_router)


@app.api_route(
    "/api/{rest:path}",
    methods=["GET", "POST", "PUT", "PATCH", "DELETE"],
    include_in_schema=False,
)
async def api_not_found(rest: str) -> None:
    """Cualquier ruta de /api que el router no matcheo.

    Va antes del mount de "/" a proposito. Sin esto la contestaria StaticFiles,
    que solo acepta GET y HEAD: un POST a un endpoint inexistente saldria 405 en
    vez de 404, y un GET saldria con el {"detail": "Not Found"} de Starlette en
    vez del formato de error del proyecto.
    """
    raise ApiError(404, "not_found", f"No existe el endpoint /api/{rest}")


app.mount(
    "/hls",
    StaticFiles(directory=str(get_settings().CACHE_DIR.resolve()), check_dir=False),
    name="hls",
)
app.mount("/static", StaticFiles(directory="static"), name="static")
app.mount("/", StaticFiles(directory="static/app", html=True, check_dir=False), name="app")
