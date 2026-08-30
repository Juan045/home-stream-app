"""Tests de la API REST."""

from __future__ import annotations

from pathlib import Path
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from app.services.job_manager import Job, JobManager
from app.services.media_analyzer import AudioTrack, SourceInfo, SubtitleTrack
from app.services.transcoder import TranscodeOptions


def _make_job(job_id: str = "abc123") -> Job:
    info = SourceInfo(
        video_codec="h264", width=1920, height=1080, duration=120.0,
        audio_codec="aac", audio_channels=2, audio_language="spa", audio_count=2,
        audio_tracks=(
            AudioTrack(index=0, codec="aac", channels=2, language="spa", title="Espanol"),
            AudioTrack(index=1, codec="ac3", channels=6, language="eng", title="English"),
        ),
        subtitle_tracks=(
            SubtitleTrack(index=0, codec="subrip", language="spa", title=""),
        ),
    )
    return Job(
        id=job_id,
        source=Path("/media/test.mkv"),
        output_dir=Path("/output") / job_id,
        info=info,
        options=TranscodeOptions(),
        current_audio_track=0,
        status="processing",
        subtitle_urls={0: f"/stream/{job_id}/subtitles/sub_0_spa.vtt"},
    )


@pytest.fixture
def app():
    from app.main import app as fastapi_app
    manager = JobManager(output_root=Path("/output"))
    job = _make_job()
    manager._jobs[job.id] = job
    fastapi_app.state.job_manager = manager
    return fastapi_app


@pytest.fixture
async def client(app):
    from httpx import ASGITransport, AsyncClient
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as ac:
        yield ac


async def test_get_job_retorna_estado(client):
    resp = await client.get("/api/v1/jobs/abc123")

    assert resp.status_code == 200
    data = resp.json()
    assert data["job_id"] == "abc123"
    assert data["status"] == "processing"
    assert len(data["audio_tracks"]) == 2
    assert data["audio_tracks"][0]["language"] == "spa"
    assert data["audio_tracks"][1]["language"] == "eng"
    assert len(data["subtitle_tracks"]) == 1
    assert data["subtitle_tracks"][0]["url"] == "/stream/abc123/subtitles/sub_0_spa.vtt"


async def test_get_job_inexistente(client):
    resp = await client.get("/api/v1/jobs/noexiste")

    assert resp.status_code == 404


async def test_post_select_cambia_audio(client, app):
    manager = app.state.job_manager
    manager.select_audio_track = AsyncMock(
        return_value=_make_job()
    )
    manager.select_audio_track.return_value.current_audio_track = 1

    resp = await client.post(
        "/api/v1/jobs/abc123/select",
        json={"audio_track": 1, "timestamp": 60.0},
    )

    assert resp.status_code == 200
    data = resp.json()
    assert data["current_audio_track"] == 1
    assert data["hls_url"] == "/stream/abc123/master.m3u8"
    manager.select_audio_track.assert_awaited_once_with("abc123", 1, 60.0)


async def test_post_select_track_invalido(client, app):
    manager = app.state.job_manager
    manager.select_audio_track = AsyncMock(
        side_effect=ValueError("Pista de audio 5 inexistente")
    )

    resp = await client.post(
        "/api/v1/jobs/abc123/select",
        json={"audio_track": 5, "timestamp": 0},
    )

    assert resp.status_code == 404
    assert "inexistente" in resp.json()["detail"]


async def test_post_stream_valida_ruta(client):
    resp = await client.post(
        "/api/v1/stream",
        json={"file_path": "relative/path.mp4"},
    )

    assert resp.status_code == 400
    assert "absoluta" in resp.json()["detail"]
