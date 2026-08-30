"""Endpoints REST para gestion de jobs y seleccion de pistas."""

from __future__ import annotations

from pathlib import Path

import structlog
from fastapi import APIRouter, HTTPException, Request

from app.models.schemas import (
    AudioTrackSchema,
    JobResponse,
    SelectRequest,
    SelectResponse,
    StreamRequest,
    SubtitleTrackSchema,
)
from app.services.job_manager import JobManager
from app.services.transcoder import TranscodeOptions

log = structlog.get_logger("api")

router = APIRouter()

ALLOWED_EXTENSIONS = frozenset({".mp4", ".mkv"})


def _get_manager(request: Request) -> JobManager:
    return request.app.state.job_manager


def _validate_path(file_path: str, media_root: Path | None) -> Path:
    """Valida la ruta del archivo segun las reglas de seguridad."""
    path = Path(file_path)

    if not path.is_absolute():
        raise HTTPException(400, detail="La ruta debe ser absoluta")

    resolved = path.resolve()
    if ".." in resolved.parts:
        raise HTTPException(400, detail="Path traversal detectado")

    if resolved.suffix.lower() not in ALLOWED_EXTENSIONS:
        raise HTTPException(
            400,
            detail=f"Extension no soportada: {resolved.suffix}. "
                   f"Permitidas: {', '.join(sorted(ALLOWED_EXTENSIONS))}",
        )

    if not resolved.exists():
        raise HTTPException(404, detail=f"El archivo no existe: {resolved}")

    if media_root is not None and not resolved.is_relative_to(media_root.resolve()):
        raise HTTPException(
            400,
            detail=f"El archivo esta fuera del directorio permitido: {media_root}",
        )

    return resolved


def _job_response(job, request: Request) -> JobResponse:
    """Convierte un Job interno a la respuesta de la API."""
    base_url = str(request.base_url).rstrip("/")
    return JobResponse(
        job_id=job.id,
        status=job.status,
        strategy=job.info.strategy.value,
        hls_url=f"/stream/{job.id}/master.m3u8",
        duration_seconds=job.info.duration,
        current_audio_track=job.current_audio_track,
        audio_tracks=[
            AudioTrackSchema(
                index=t.index,
                codec=t.codec,
                channels=t.channels,
                language=t.language,
                title=t.title,
            )
            for t in job.info.audio_tracks
        ],
        subtitle_tracks=[
            SubtitleTrackSchema(
                index=t.index,
                codec=t.codec,
                language=t.language,
                title=t.title,
                url=job.subtitle_urls.get(t.index),
            )
            for t in job.info.subtitle_tracks
        ],
        error=job.error,
    )


@router.post("/stream", status_code=202)
async def create_stream(body: StreamRequest, request: Request) -> JobResponse:
    """Crea un nuevo job de streaming para el archivo indicado."""
    from app.config import get_settings
    settings = get_settings()

    source = _validate_path(body.file_path, settings.MEDIA_ROOT)
    manager = _get_manager(request)

    options = TranscodeOptions(
        hls_time=settings.HLS_TIME,
        crf=settings.FFMPEG_CRF,
        preset=settings.FFMPEG_PRESET,
        audio_bitrate=settings.AUDIO_BITRATE,
        audio_channels=settings.AUDIO_CHANNELS,
    )

    try:
        job = await manager.create_job(source, options)
    except Exception as exc:
        log.error("error creando job", error=str(exc))
        raise HTTPException(500, detail=str(exc))

    log.info("stream creado", job_id=job.id, source=str(source))
    return _job_response(job, request)


@router.get("/jobs/{job_id}")
async def get_job(job_id: str, request: Request) -> JobResponse:
    """Devuelve el estado actual del job."""
    manager = _get_manager(request)
    job = manager.get_job(job_id)
    if job is None:
        raise HTTPException(404, detail=f"Job {job_id} no existe")
    return _job_response(job, request)


@router.post("/jobs/{job_id}/select")
async def select_track(job_id: str, body: SelectRequest, request: Request) -> SelectResponse:
    """Cambia la pista de audio y/o subtitulos del job."""
    manager = _get_manager(request)
    job = manager.get_job(job_id)
    if job is None:
        raise HTTPException(404, detail=f"Job {job_id} no existe")

    if body.audio_track is not None:
        try:
            job = await manager.select_audio_track(
                job_id, body.audio_track, body.timestamp,
            )
        except ValueError as exc:
            raise HTTPException(404, detail=str(exc))
        except Exception as exc:
            log.error("error cambiando audio", job_id=job_id, error=str(exc))
            raise HTTPException(500, detail=str(exc))

    return SelectResponse(
        job_id=job.id,
        status=job.status,
        current_audio_track=job.current_audio_track,
        hls_url=f"/stream/{job.id}/master.m3u8",
    )
