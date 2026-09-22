"""Tests de media_analyzer: ffprobe mockeado, nunca se llama al binario real."""

from __future__ import annotations

from dataclasses import replace
from pathlib import Path

import pytest

from app.errors import FFmpegError
from app.services import media_analyzer
from app.services.media_analyzer import (
    StreamStrategy,
    analyze,
    parse_probe,
    probe,
    with_ignored,
)
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


@pytest.mark.parametrize("transfer", ["smpte2084", "arib-std-b67"])
def test_parse_probe_detecta_hdr_por_transferencia(transfer):
    info = parse_probe(_loads(probe_payload(
        color_primaries="bt2020",
        color_transfer=transfer,
        color_space="bt2020nc",
    )))

    assert info.is_hdr is True
    assert info.video_color_primaries == "bt2020"
    assert info.video_color_space == "bt2020nc"


def test_parse_probe_sdr_no_se_clasifica_como_hdr():
    info = parse_probe(_loads(probe_payload(
        color_primaries="bt709", color_transfer="bt709", color_space="bt709",
    )))

    assert info.is_hdr is False


def test_metadata_de_color_sobrevive_la_serializacion(hevc_ac3):
    hdr = media_analyzer.from_dict(media_analyzer.to_dict(
        replace(
            hevc_ac3,
            video_color_primaries="bt2020",
            video_color_transfer="smpte2084",
            video_color_space="bt2020nc",
        )
    ))

    assert hdr.is_hdr is True
    assert hdr.video_color_primaries == "bt2020"


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


# --- Seleccion de pistas ----------------------------------------------------
#
# `with_ignored` es logica pura: marca el flag que despues decide que pistas
# genera FFmpeg. Sin disco, sin ffprobe y sin BD.

def _flags(tracks) -> list[tuple[int, bool]]:
    return [(t.index, t.ignore) for t in tracks]


def test_por_defecto_ninguna_pista_se_ignora(hevc_ac3):
    assert _flags(hevc_ac3.audio_tracks) == [(0, False), (1, False)]
    assert _flags(hevc_ac3.subtitle_tracks) == [(0, False)]


def test_la_lista_reemplaza_al_estado_anterior(hevc_ac3):
    """Mandar `[]` vuelve a generar todo: no es "no tocar", es "ninguna"."""
    marcado = with_ignored(hevc_ac3, audio=[1])
    assert _flags(marcado.audio_tracks) == [(0, False), (1, True)]

    limpio = with_ignored(marcado, audio=[])
    assert _flags(limpio.audio_tracks) == [(0, False), (1, False)]


def test_none_deja_esa_clase_de_pista_como_estaba(hevc_ac3):
    marcado = with_ignored(hevc_ac3, audio=[0], subtitles=[0])

    solo_audio = with_ignored(marcado, audio=[])

    assert _flags(solo_audio.audio_tracks) == [(0, False), (1, False)]
    assert _flags(solo_audio.subtitle_tracks) == [(0, True)]


def test_un_indice_inexistente_no_hace_nada(hevc_ac3):
    """La lista marca las pistas que reporto ffprobe; nunca las indexa."""
    marcado = with_ignored(hevc_ac3, audio=[99])

    assert _flags(marcado.audio_tracks) == [(0, False), (1, False)]


def test_el_flag_sobrevive_la_serializacion(hevc_ac3):
    marcado = with_ignored(hevc_ac3, subtitles=[0])

    ida_y_vuelta = media_analyzer.from_dict(media_analyzer.to_dict(marcado))

    assert _flags(ida_y_vuelta.subtitle_tracks) == [(0, True)]


def test_una_ficha_vieja_sin_el_campo_se_lee_igual(hevc_ac3):
    """El default cubre a las fichas guardadas antes de que existiera."""
    viejo = media_analyzer.to_dict(hevc_ac3)
    for track in (*viejo["audio_tracks"], *viejo["subtitle_tracks"]):
        del track["ignore"]

    info = media_analyzer.from_dict(viejo)

    assert _flags(info.audio_tracks) == [(0, False), (1, False)]
