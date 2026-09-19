"""Tests del registro de encoders y del perfil de biblioteca.

Logica pura sobre los argumentos: no hay FFmpeg ni disco. Lo que se fija aca es
que el perfil de `ESPECIFICACION.md` salga tal cual en el comando, y que
agregar un encoder siga siendo agregar una entrada al diccionario.
"""

from __future__ import annotations

from dataclasses import replace
from pathlib import Path

import pytest

from app import codecs
from app.services.transcoder import TranscodeOptions, build_video_args


@pytest.fixture
def archive() -> TranscodeOptions:
    return replace(TranscodeOptions(), **codecs.ARCHIVE)


def video_args(options: TranscodeOptions, info) -> list[str]:
    return build_video_args(Path("/media/x.mkv"), Path("/out/video"), info, options)


# --- Registro ---------------------------------------------------------------

def test_los_dos_encoders_estan_registrados():
    assert set(codecs.VIDEO) == {"h264", "av1"}
    assert codecs.DEFAULT_VIDEO in codecs.VIDEO


def test_un_encoder_desconocido_falla_nombrando_los_que_hay():
    with pytest.raises(ValueError) as exc:
        codecs.video("vp9")

    assert "av1" in str(exc.value) and "h264" in str(exc.value)


def test_solo_h264_declara_su_codec_en_el_master():
    # AV1 no fija `-level`, asi que declarar el CODECS seria mentir en el nivel:
    # es preferible omitirlo y que el player lo deduzca del init segment.
    assert codecs.video("h264").codec_string == "avc1.640029"
    assert codecs.video("av1").codec_string is None


# --- Perfil de biblioteca ---------------------------------------------------

def test_el_perfil_es_el_de_la_especificacion(archive):
    assert archive.video_codec == "av1"
    assert archive.crf == 32
    assert archive.preset == "6"


def test_el_perfil_hereda_lo_que_no_fija():
    base = TranscodeOptions(hls_time=10, audio_bitrate="192k", audio_channels=6)

    archive = replace(base, **codecs.ARCHIVE)

    # Segmento y audio siguen saliendo de la configuracion del server: el perfil
    # solo decide como se codifica el video.
    assert archive.hls_time == 10
    assert archive.audio_bitrate == "192k"
    assert archive.audio_channels == 6


def test_el_comando_de_archivo_es_el_de_la_especificacion(archive, hevc_ac3):
    args = video_args(archive, hevc_ac3)

    assert ["-c:v", "libsvtav1"] == args[args.index("-c:v"):args.index("-c:v") + 2]
    assert args[args.index("-preset") + 1] == "6"
    assert args[args.index("-crf") + 1] == "32"
    assert args[args.index("-pix_fmt") + 1] == "yuv420p"


def test_el_perfil_de_archivo_no_escala(archive, hevc_ac3):
    # Una copia que se guarda para siempre conserva la resolucion del master:
    # el 4K de la fuente no se recorta a 1080p.
    assert "-vf" not in video_args(archive, hevc_ac3)


def test_el_streaming_sigue_capando_la_resolucion(hevc_ac3):
    args = video_args(TranscodeOptions(), hevc_ac3)

    assert "min(1080,ih)" in args[args.index("-vf") + 1]


def test_el_archivo_codifica_aunque_el_origen_sea_h264(archive, h264_aac):
    # Sin `force_transcode` una pelicula que ya es H.264 se copiaria, y el
    # encode de biblioteca no produciria AV1.
    args = video_args(archive, h264_aac)

    assert "libsvtav1" in args
    assert ["-c:v", "copy"] != args[args.index("-c:v"):args.index("-c:v") + 2]


def test_el_archivo_corta_en_los_bordes_de_segmento(archive, hevc_ac3):
    # Es la invariante de la que dependen las duraciones de la playlist, y vale
    # igual para AV1 que para H.264.
    args = video_args(archive, hevc_ac3)

    assert args[args.index("-force_key_frames") + 1] == "expr:gte(t,n_forced*6)"


def test_el_archivo_conserva_el_timeline_absoluto(archive, hevc_ac3):
    args = video_args(archive, hevc_ac3)

    assert "-copyts" in args
    assert "-output_ts_offset" not in args
    assert "-start_at_zero" not in args


# --- Preset de SVT-AV1 ------------------------------------------------------

def test_el_preset_numerico_pasa_tal_cual():
    # El perfil de biblioteca fija el 6 de la especificacion, que no tiene
    # nombre equivalente en la escala de x264.
    assert codecs.svtav1_preset("6") == "6"


def test_el_preset_por_nombre_se_traduce():
    # `SM_FFMPEG_PRESET` viene con los nombres de x264 y SVT-AV1 quiere numeros.
    assert codecs.svtav1_preset("veryfast") == "10"
    assert codecs.svtav1_preset("slow") == "5"


def test_un_preset_desconocido_cae_en_el_default():
    assert codecs.svtav1_preset("turbo") == str(codecs.SVTAV1_DEFAULT_PRESET)
