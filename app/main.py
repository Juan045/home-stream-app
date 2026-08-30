"""Entry point de la aplicacion FastAPI."""

from __future__ import annotations

import mimetypes
from contextlib import asynccontextmanager

import structlog
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles
from starlette.middleware.base import BaseHTTPMiddleware
from starlette.requests import Request
from starlette.responses import Response

from app.api.routes import router
from app.config import get_settings
from app.log import setup as setup_logging
from app.services.job_manager import JobManager

log = structlog.get_logger("main")

mimetypes.add_type("text/vtt", ".vtt")
mimetypes.add_type("application/vnd.apple.mpegurl", ".m3u8")


class CacheControlMiddleware(BaseHTTPMiddleware):
    """Cache inmutable para segmentos .ts, no-store para manifests y subs."""

    async def dispatch(self, request: Request, call_next) -> Response:
        response = await call_next(request)
        path = request.url.path
        if path.endswith(".ts"):
            response.headers["Cache-Control"] = "public, max-age=31536000, immutable"
        elif path.endswith((".m3u8", ".vtt")):
            response.headers["Cache-Control"] = "no-store"
        return response


@asynccontextmanager
async def lifespan(app: FastAPI):
    settings = get_settings()
    setup_logging(debug=False)

    output_dir = settings.OUTPUT_DIR.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    app.state.job_manager = JobManager(output_root=output_dir)

    app.mount("/stream", StaticFiles(directory=str(output_dir)), name="stream")
    app.mount("/static", StaticFiles(directory="static"), name="static")

    log.info("servidor iniciado", output_dir=str(output_dir), port=settings.PORT)
    yield

    await app.state.job_manager.shutdown()
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

app.include_router(router, prefix="/api/v1")
