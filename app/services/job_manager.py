"""Gestion de jobs: estado en memoria y ciclo de vida del proceso FFmpeg."""

from __future__ import annotations

import asyncio
from dataclasses import dataclass, field
from pathlib import Path
from uuid import uuid4

import structlog

from app.errors import FFmpegError
from app.services.media_analyzer import SourceInfo, analyze
from app.services.transcoder import (
    ManagedFFmpeg,
    TranscodeOptions,
    build_args,
    extract_subtitle,
    start_ffmpeg,
)

log = structlog.get_logger("job_manager")

MANIFEST_NAME = "master.m3u8"
MANIFEST_POLL_INTERVAL = 0.2
MANIFEST_POLL_TIMEOUT = 15.0


@dataclass
class Job:
    id: str
    source: Path
    output_dir: Path
    info: SourceInfo
    options: TranscodeOptions
    current_audio_track: int
    status: str
    ffmpeg: ManagedFFmpeg | None = None
    subtitle_urls: dict[int, str] = field(default_factory=dict)
    error: str | None = None


class JobManager:
    def __init__(self, output_root: Path) -> None:
        self._jobs: dict[str, Job] = {}
        self._locks: dict[str, asyncio.Lock] = {}
        self._output_root = output_root

    async def create_job(self, source: Path, options: TranscodeOptions) -> Job:
        """Analiza el archivo, extrae subtitulos, inicia FFmpeg."""
        job_id = uuid4().hex[:12]
        output_dir = self._output_root / job_id
        output_dir.mkdir(parents=True, exist_ok=True)

        info = await analyze(source, options.audio_track)

        subtitle_urls = await self._extract_subtitles(source, output_dir, info, job_id)

        job = Job(
            id=job_id,
            source=source,
            output_dir=output_dir,
            info=info,
            options=options,
            current_audio_track=options.audio_track,
            status="processing",
            subtitle_urls=subtitle_urls,
        )

        args = build_args(source, output_dir, info, options)
        job.ffmpeg = await start_ffmpeg(args)
        asyncio.create_task(self._watch_ffmpeg(job))

        self._jobs[job_id] = job
        self._locks[job_id] = asyncio.Lock()

        log.info(
            "job creado",
            job_id=job_id,
            source=str(source),
            strategy=info.strategy.value,
            audio_tracks=len(info.audio_tracks),
            subtitle_tracks=len(info.subtitle_tracks),
        )
        return job

    async def select_audio_track(
        self, job_id: str, audio_track: int, timestamp: float = 0,
    ) -> Job:
        """Cambia la pista de audio: mata FFmpeg, reinicia con -map nuevo."""
        job = self._get_or_raise(job_id)
        lock = self._locks[job_id]

        async with lock:
            if audio_track >= len(job.info.audio_tracks):
                raise ValueError(
                    f"Pista de audio {audio_track} inexistente "
                    f"(el archivo tiene {len(job.info.audio_tracks)})"
                )

            if job.ffmpeg and job.ffmpeg.is_running:
                log.info("matando ffmpeg para cambio de audio", job_id=job_id)
                await job.ffmpeg.kill()

            _clear_segments(job.output_dir)

            from dataclasses import replace
            new_options = replace(
                job.options,
                audio_track=audio_track,
                seek_seconds=timestamp if timestamp > 0 else None,
                start_number=int(timestamp / job.options.hls_time) if timestamp > 0 else 0,
            )

            args = build_args(job.source, job.output_dir, job.info, new_options)
            job.ffmpeg = await start_ffmpeg(args)
            job.current_audio_track = audio_track
            job.options = new_options
            job.status = "processing"
            job.error = None

            asyncio.create_task(self._watch_ffmpeg(job))

            await self._wait_for_manifest(job.output_dir)

            log.info(
                "audio cambiado",
                job_id=job_id,
                audio_track=audio_track,
                timestamp=timestamp,
            )
            return job

    def get_job(self, job_id: str) -> Job | None:
        return self._jobs.get(job_id)

    def all_jobs(self) -> list[Job]:
        return list(self._jobs.values())

    def _get_or_raise(self, job_id: str) -> Job:
        job = self._jobs.get(job_id)
        if job is None:
            raise KeyError(f"Job {job_id} no existe")
        return job

    async def _watch_ffmpeg(self, job: Job) -> None:
        """Espera a que FFmpeg termine y actualiza el estado."""
        try:
            await job.ffmpeg.wait()
            job.status = "ready"
            log.info("ffmpeg completado", job_id=job.id)
        except FFmpegError as exc:
            job.status = "failed"
            job.error = exc.stderr
            log.error("ffmpeg fallo", job_id=job.id, stderr_tail=exc.stderr[-200:])
        except asyncio.CancelledError:
            pass

    async def _extract_subtitles(
        self, source: Path, output_dir: Path, info: SourceInfo, job_id: str,
    ) -> dict[int, str]:
        """Extrae todas las pistas de subtitulos a WebVTT."""
        urls: dict[int, str] = {}
        for track in info.subtitle_tracks:
            vtt_name = f"sub_{track.index}_{track.language}.vtt"
            vtt_path = output_dir / "subtitles" / vtt_name
            try:
                await extract_subtitle(source, vtt_path, track.index)
                urls[track.index] = f"/stream/{job_id}/subtitles/{vtt_name}"
            except FFmpegError as exc:
                log.warning(
                    "no se pudo extraer subtitulo",
                    job_id=job_id,
                    track=track.index,
                    error=str(exc),
                )
        return urls

    async def _wait_for_manifest(self, output_dir: Path) -> None:
        """Espera a que FFmpeg escriba el manifest inicial."""
        manifest = output_dir / MANIFEST_NAME
        elapsed = 0.0
        while elapsed < MANIFEST_POLL_TIMEOUT:
            if manifest.exists() and manifest.stat().st_size > 0:
                return
            await asyncio.sleep(MANIFEST_POLL_INTERVAL)
            elapsed += MANIFEST_POLL_INTERVAL
        log.warning("timeout esperando manifest", path=str(manifest))

    async def shutdown(self) -> None:
        """Mata todos los procesos FFmpeg activos."""
        for job in self._jobs.values():
            if job.ffmpeg and job.ffmpeg.is_running:
                await job.ffmpeg.kill()
        log.info("todos los procesos ffmpeg detenidos")


def _clear_segments(output_dir: Path) -> None:
    """Borra segmentos .ts y manifest .m3u8, preserva subtitles/."""
    for pattern in ("*.ts", "*.m3u8"):
        for f in output_dir.glob(pattern):
            f.unlink()
