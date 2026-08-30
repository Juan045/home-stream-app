"""Tests de job_manager: creacion de jobs y cambio de pista de audio."""

from __future__ import annotations

from pathlib import Path
from unittest.mock import AsyncMock, patch

import pytest

from app.services.job_manager import JobManager, _clear_segments
from app.services.media_analyzer import AudioTrack, SourceInfo, SubtitleTrack
from app.services.transcoder import ManagedFFmpeg, TranscodeOptions


@pytest.fixture
def output_root(tmp_path):
    root = tmp_path / "output"
    root.mkdir()
    return root


def test_clear_segments_preserva_subtitulos(tmp_path):
    (tmp_path / "segment_00000.ts").write_bytes(b"data")
    (tmp_path / "segment_00001.ts").write_bytes(b"data")
    (tmp_path / "master.m3u8").write_text("#EXTM3U")
    subs_dir = tmp_path / "subtitles"
    subs_dir.mkdir()
    (subs_dir / "sub_0_spa.vtt").write_text("WEBVTT")

    _clear_segments(tmp_path)

    assert not list(tmp_path.glob("*.ts"))
    assert not list(tmp_path.glob("*.m3u8"))
    assert (subs_dir / "sub_0_spa.vtt").exists()


async def test_create_job_inicia_ffmpeg(output_root, h264_aac, spawn_mock):
    from app.services import transcoder
    from tests.conftest import FakeProcess

    fake_ffmpeg = FakeProcess(stdout_lines=[b"out_time_us=6000000\n"])
    spawn_mock(transcoder, fake_ffmpeg)

    manager = JobManager(output_root=output_root)
    source = Path("/media/test.mkv")

    with patch("app.services.job_manager.analyze", new_callable=AsyncMock, return_value=h264_aac):
        job = await manager.create_job(source, TranscodeOptions())

    assert job.id
    assert job.status in ("processing", "ready")
    assert job.current_audio_track == 0
    assert job.ffmpeg is not None


async def test_select_audio_track_mata_y_reinicia(output_root, spawn_mock):
    import asyncio
    from app.services import transcoder
    from tests.conftest import FakeProcess

    info = SourceInfo(
        video_codec="h264", width=1920, height=1080, duration=120.0,
        audio_codec="aac", audio_channels=2, audio_language="spa", audio_count=2,
        audio_tracks=(
            AudioTrack(index=0, codec="aac", channels=2, language="spa", title=""),
            AudioTrack(index=1, codec="ac3", channels=6, language="eng", title=""),
        ),
        subtitle_tracks=(),
    )

    fake_ffmpeg = FakeProcess(stdout_lines=[b"out_time_us=6000000\n"])
    spawn_mock(transcoder, fake_ffmpeg)

    manager = JobManager(output_root=output_root)

    with patch("app.services.job_manager.analyze", new_callable=AsyncMock, return_value=info):
        job = await manager.create_job(Path("/media/test.mkv"), TranscodeOptions())

    job_dir = job.output_dir
    (job_dir / "segment_00000.ts").write_bytes(b"data")
    (job_dir / "master.m3u8").write_text("#EXTM3U")

    fake_ffmpeg2 = FakeProcess(stdout_lines=[b"out_time_us=1000000\n"])
    spawn_mock(transcoder, fake_ffmpeg2)

    with patch.object(manager, "_wait_for_manifest", new_callable=AsyncMock):
        job = await manager.select_audio_track(job.id, 1, timestamp=60.0)

    assert job.current_audio_track == 1
    assert not list(job_dir.glob("*.ts"))


async def test_select_audio_track_invalido(output_root, spawn_mock):
    from app.services import transcoder
    from tests.conftest import FakeProcess

    info = SourceInfo(
        video_codec="h264", width=1920, height=1080, duration=120.0,
        audio_codec="aac", audio_channels=2, audio_language="spa", audio_count=1,
        audio_tracks=(
            AudioTrack(index=0, codec="aac", channels=2, language="spa", title=""),
        ),
        subtitle_tracks=(),
    )

    fake_ffmpeg = FakeProcess(stdout_lines=[])
    spawn_mock(transcoder, fake_ffmpeg)

    manager = JobManager(output_root=output_root)

    with patch("app.services.job_manager.analyze", new_callable=AsyncMock, return_value=info):
        job = await manager.create_job(Path("/media/test.mkv"), TranscodeOptions())

    with pytest.raises(ValueError, match="inexistente"):
        await manager.select_audio_track(job.id, 5, timestamp=0)
