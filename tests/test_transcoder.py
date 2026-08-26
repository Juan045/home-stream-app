"""Tests de transcoder: se verifica el comando armado y el proceso mockeado."""

from __future__ import annotations

from dataclasses import replace
from pathlib import Path

import pytest

from app.errors import FFmpegError
from app.services import transcoder
from app.services.transcoder import (
    TranscodeOptions,
    build_args,
    parse_progress_line,
    run_ffmpeg,
)
from tests.conftest import FakeProcess

SOURCE = Path("/media/video.mkv")
OUTPUT = Path("/app/output")


def args_for(info, **overrides) -> list[str]:
    return build_args(SOURCE, OUTPUT, info, TranscodeOptions(**overrides))


def pair_after(args: list[str], flag: str) -> str:
    """Devuelve el valor que sigue a `flag` en la lista de argumentos."""
    return args[args.index(flag) + 1]


# --- Seleccion de codecs ----------------------------------------------------

def test_h264_aac_se_remuxea_sin_recodificar(h264_aac):
    args = args_for(h264_aac)

    assert pair_after(args, "-c:v") == "copy"
    assert pair_after(args, "-c:a") == "copy"
    assert "libx264" not in args


def test_hevc_se_transcodifica_a_h264(hevc_ac3):
    args = args_for(hevc_ac3)

    assert pair_after(args, "-c:v") == "libx264"
    assert pair_after(args, "-crf") == "23"
    assert pair_after(args, "-preset") == "veryfast"
    assert pair_after(args, "-pix_fmt") == "yuv420p"


def test_audio_no_aac_se_transcodifica_a_aac(hevc_ac3):
    args = args_for(hevc_ac3)

    assert pair_after(args, "-c:a") == "aac"
    assert pair_after(args, "-b:a") == "128k"
    assert pair_after(args, "-ac") == "2"


def test_aac_multicanal_se_downmixea(h264_aac):
    multicanal = replace(h264_aac, audio_channels=6)

    args = args_for(multicanal)

    assert pair_after(args, "-c:a") == "aac"
    assert pair_after(args, "-ac") == "2"


def test_force_transcode_ignora_el_remux(h264_aac):
    args = args_for(h264_aac, force_transcode=True)

    assert pair_after(args, "-c:v") == "libx264"
    assert pair_after(args, "-c:a") == "aac"


def test_sin_audio_agrega_an(h264_aac):
    sin_audio = replace(h264_aac, audio_codec=None, audio_channels=None, audio_count=0)

    args = args_for(sin_audio)

    assert "-an" in args
    assert "-map" in args
    assert not any(a.startswith("0:a:") for a in args)


# --- Mapeo de pistas y subtitulos -------------------------------------------

@pytest.mark.parametrize("track", [0, 1, 3])
def test_mapea_la_pista_de_audio_seleccionada(hevc_ac3, track):
    args = args_for(hevc_ac3, audio_track=track)

    assert "0:v:0" in args
    assert f"0:a:{track}" in args


def test_descarta_subtitulos_y_capitulos(h264_aac):
    args = args_for(h264_aac)

    assert "-sn" in args
    assert "-dn" in args
    assert pair_after(args, "-map_chapters") == "-1"
    assert "webvtt" not in args


# --- Empaquetado HLS --------------------------------------------------------

def test_salida_hls_con_manifest_y_segmentos(h264_aac):
    args = args_for(h264_aac)

    assert pair_after(args, "-f") == "hls"
    assert pair_after(args, "-hls_time") == "6"
    assert pair_after(args, "-hls_list_size") == "0"
    assert pair_after(args, "-hls_playlist_type") == "event"
    assert pair_after(args, "-hls_segment_filename") == str(OUTPUT / "segment_%05d.ts")
    assert args[-1] == str(OUTPUT / "master.m3u8")
    assert "-y" in args


def test_hls_time_alinea_los_keyframes(hevc_ac3):
    args = args_for(hevc_ac3, hls_time=4)

    assert pair_after(args, "-hls_time") == "4"
    assert pair_after(args, "-force_key_frames") == "expr:gte(t,n_forced*4)"


def test_progreso_por_stdout(h264_aac):
    args = args_for(h264_aac)

    assert pair_after(args, "-progress") == "pipe:1"
    assert pair_after(args, "-i") == str(SOURCE)


# --- Parseo de progreso -----------------------------------------------------

@pytest.mark.parametrize(
    ("linea", "esperado"),
    [
        ("out_time_us=90000000", 90.0),
        ("out_time_ms=1500000\n", 1.5),
        ("frame=120", None),
        ("out_time=00:01:30.000000", None),
        ("out_time_us=N/A", None),
        ("progress=continue", None),
    ],
)
def test_parse_progress_line(linea, esperado):
    assert parse_progress_line(linea) == esperado


# --- Ejecucion --------------------------------------------------------------

async def test_run_ffmpeg_reporta_progreso(spawn_mock):
    process = FakeProcess(
        stdout_lines=[b"frame=1\n", b"out_time_us=6000000\n", b"out_time_us=12000000\n",
                      b"progress=end\n"],
    )
    calls = spawn_mock(transcoder, process)

    vistos: list[float] = []
    await run_ffmpeg(["-i", "in.mkv"], on_progress=vistos.append)

    assert calls[0][0] == "ffmpeg"
    assert calls[0][1:] == ("-i", "in.mkv")
    assert vistos == [6.0, 12.0]


async def test_run_ffmpeg_no_duplica_us_y_ms(spawn_mock):
    """FFmpeg emite out_time_us y out_time_ms con el mismo valor por bloque."""
    spawn_mock(transcoder, FakeProcess(stdout_lines=[
        b"out_time_us=6000000\n", b"out_time_ms=6000000\n", b"progress=continue\n",
        b"out_time_us=12000000\n", b"out_time_ms=12000000\n", b"progress=end\n",
    ]))

    vistos: list[float] = []
    await run_ffmpeg(["-i", "in.mkv"], on_progress=vistos.append)

    assert vistos == [6.0, 12.0]


async def test_run_ffmpeg_sin_callback_no_falla(spawn_mock):
    spawn_mock(transcoder, FakeProcess(stdout_lines=[b"out_time_us=1000000\n"]))

    await run_ffmpeg(["-i", "in.mkv"])


async def test_run_ffmpeg_falla_con_stderr(spawn_mock):
    spawn_mock(
        transcoder,
        FakeProcess(
            returncode=1,
            stderr_lines=[b"Unknown encoder 'libx264'\n", b"Conversion failed\n"],
        ),
    )

    with pytest.raises(FFmpegError) as exc:
        await run_ffmpeg(["-i", "in.mkv"])

    assert exc.value.returncode == 1
    assert "Unknown encoder" in exc.value.stderr
    assert "Conversion failed" in exc.value.stderr


async def test_run_ffmpeg_recorta_el_stderr(spawn_mock):
    ruido = [f"linea {i}\n".encode() for i in range(200)]
    spawn_mock(transcoder, FakeProcess(returncode=1, stderr_lines=ruido))

    with pytest.raises(FFmpegError) as exc:
        await run_ffmpeg(["-i", "in.mkv"])

    lineas = exc.value.stderr.splitlines()
    assert len(lineas) == transcoder.STDERR_TAIL_LINES
    assert lineas[-1] == "linea 199"
