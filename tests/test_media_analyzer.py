"""Tests de media_analyzer: ffprobe mockeado, nunca se llama al binario real."""

from __future__ import annotations

from pathlib import Path

import pytest

from app.errors import FFmpegError
from app.services import media_analyzer
from app.services.media_analyzer import StreamStrategy, analyze, parse_probe, probe
from tests.conftest import FakeProcess, probe_payload


async def test_probe_invoca_ffprobe_con_json(spawn_mock):
    calls = spawn_mock(media_analyzer, FakeProcess(stdout=probe_payload()))

    info = await probe(Path("/media/video.mkv"))

    (cmd,) = calls
    assert cmd[0] == "ffprobe"
    assert "-print_format" in cmd and "json" in cmd
    assert "-show_streams" in cmd and "-show_format" in cmd
    assert cmd[-1] == str(Path("/media/video.mkv"))
    assert info["format"]["duration"] == "120.5"


async def test_probe_falla_incluye_stderr(spawn_mock):
    spawn_mock(
        media_analyzer,
        FakeProcess(returncode=1, stderr=b"Invalid data found when processing input"),
    )

    with pytest.raises(FFmpegError) as exc:
        await probe(Path("/media/roto.mkv"))

    assert exc.value.command == "ffprobe"
    assert exc.value.returncode == 1
    assert "Invalid data" in exc.value.stderr


async def test_analyze_devuelve_source_info(spawn_mock):
    spawn_mock(
        media_analyzer,
        FakeProcess(stdout=probe_payload(video_codec="hevc", audio_codecs=("ac3", "aac"),
                                         channels=6)),
    )

    info = await analyze(Path("/media/video.mkv"))

    assert info.video_codec == "hevc"
    assert info.audio_codec == "ac3"
    assert info.audio_channels == 6
    assert info.audio_count == 2
    assert info.duration == pytest.approx(120.5)


def test_parse_probe_elige_la_pista_de_audio_pedida():
    payload = probe_payload(audio_codecs=("ac3", "aac"))

    primera = parse_probe(_loads(payload), audio_track=0)
    segunda = parse_probe(_loads(payload), audio_track=1)

    assert primera.audio_codec == "ac3"
    assert primera.audio_language == "spa"
    assert segunda.audio_codec == "aac"
    assert segunda.audio_language == "eng"


def test_parse_probe_rechaza_pista_inexistente():
    with pytest.raises(ValueError, match="inexistente"):
        parse_probe(_loads(probe_payload(audio_codecs=("aac",))), audio_track=3)


def test_parse_probe_rechaza_archivo_sin_video():
    with pytest.raises(ValueError, match="no tiene pistas de video"):
        parse_probe({"streams": [{"codec_type": "audio", "codec_name": "aac"}]})


def test_parse_probe_sin_audio():
    info = parse_probe(_loads(probe_payload(audio_codecs=())))

    assert info.has_audio is False
    assert info.audio_codec is None
    assert info.audio_count == 0


def test_parse_probe_duracion_invalida_es_cero():
    info = parse_probe(_loads(probe_payload(duration="N/A")))

    assert info.duration == 0.0


@pytest.mark.parametrize(
    ("codec", "esperada"),
    [
        ("h264", StreamStrategy.REMUX),
        ("hevc", StreamStrategy.TRANSCODE),
        ("vp9", StreamStrategy.TRANSCODE),
        ("mpeg4", StreamStrategy.TRANSCODE),
    ],
)
def test_estrategia_segun_codec(codec, esperada):
    info = parse_probe(_loads(probe_payload(video_codec=codec)))

    assert info.strategy is esperada


def test_audio_browser_ready(h264_aac, hevc_ac3):
    assert h264_aac.audio_is_browser_ready is True
    assert hevc_ac3.audio_is_browser_ready is False


def _loads(payload: bytes) -> dict:
    import json

    return json.loads(payload)
