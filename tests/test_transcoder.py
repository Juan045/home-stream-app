"""Tests de transcoder: se verifica el comando armado y el proceso mockeado."""

from __future__ import annotations

import asyncio
from dataclasses import replace
from pathlib import Path

import pytest

from app.errors import FFmpegError
from app.services import transcoder
from app.services.media_analyzer import AudioTrack
from app.services.transcoder import (
    TranscodeOptions,
    build_audio_args,
    build_subtitle_args,
    build_video_args,
    parse_progress_line,
    run_ffmpeg,
    start_ffmpeg,
)
from tests.conftest import FakeProcess

SOURCE = Path("/media/video.mkv")
OUTPUT = Path("/app/output")


def video_args_for(info, **overrides) -> list[str]:
    return build_video_args(SOURCE, OUTPUT, info, TranscodeOptions(**overrides))


def audio_args_for(info, track: int = 0, **overrides) -> list[str]:
    return build_audio_args(SOURCE, OUTPUT, info, TranscodeOptions(**overrides), track)


def pair_after(args: list[str], flag: str) -> str:
    """Devuelve el valor que sigue a `flag` en la lista de argumentos."""
    return args[args.index(flag) + 1]


# --- Timeline absoluto -------------------------------------------------------
#
# El video, cada pista de audio y los subtitulos se generan en corridas
# separadas de FFmpeg. Que compartan el mismo origen de tiempo es lo unico que
# los mantiene sincronizados; estos tests son la red de seguridad.

@pytest.mark.parametrize("build", [video_args_for, audio_args_for])
def test_copyts_va_antes_del_input(h264_aac, build):
    args = build(h264_aac)

    assert "-copyts" in args
    assert args.index("-copyts") < args.index("-i")


@pytest.mark.parametrize("build", [video_args_for, audio_args_for])
def test_no_rebasea_ni_desplaza_los_timestamps(h264_aac, build):
    args = build(h264_aac)

    assert pair_after(args, "-avoid_negative_ts") == "disabled"
    assert pair_after(args, "-muxdelay") == "0"
    assert pair_after(args, "-muxpreload") == "0"


@pytest.mark.parametrize("build", [video_args_for, audio_args_for])
def test_no_usa_output_ts_offset_ni_start_at_zero(h264_aac, build):
    # Con -copyts los PTS ya salen absolutos: -output_ts_offset los duplicaria
    # y -start_at_zero lo contradice. Ninguno de los dos debe aparecer.
    args = build(h264_aac)

    assert "-output_ts_offset" not in args
    assert "-start_at_zero" not in args


@pytest.mark.parametrize("build", [video_args_for, audio_args_for])
def test_no_hay_seek_en_las_corridas_completas(h264_aac, build):
    # El archivo se procesa entero de una vez; el seek es del cliente.
    args = build(h264_aac)

    assert "-ss" not in args
    assert "-start_number" not in args


# --- Salida fMP4 -------------------------------------------------------------

@pytest.mark.parametrize("build", [video_args_for, audio_args_for])
def test_salida_en_fmp4_con_init_segment(h264_aac, build):
    args = build(h264_aac)

    assert pair_after(args, "-f") == "hls"
    assert pair_after(args, "-hls_segment_type") == "fmp4"
    assert pair_after(args, "-hls_fmp4_init_filename") == "init.mp4"


@pytest.mark.parametrize("build", [video_args_for, audio_args_for])
def test_la_playlist_interna_se_escribe_incrementalmente(h264_aac, build):
    # Con -hls_playlist_type vod FFmpeg acumula la playlist y la escribe recien
    # al cerrar: durante todo el build no habria de donde leer las duraciones
    # aunque los segmentos ya existan. Con -hls_list_size 0 la reescribe al
    # cerrar cada segmento.
    args = build(h264_aac)

    assert pair_after(args, "-hls_list_size") == "0"
    assert "-hls_playlist_type" not in args


@pytest.mark.parametrize("build", [video_args_for, audio_args_for])
def test_escribe_el_playlist_interno_y_los_segmentos(h264_aac, build):
    args = build(h264_aac)

    assert pair_after(args, "-hls_segment_filename").endswith("seg-%05d.m4s")
    assert args[-1].endswith("internal.m3u8")


@pytest.mark.parametrize("build", [video_args_for, audio_args_for])
def test_hls_time_configurable(h264_aac, build):
    assert pair_after(build(h264_aac, hls_time=4), "-hls_time") == "4"


@pytest.mark.parametrize("build", [video_args_for, audio_args_for])
def test_reporta_progreso_por_stdout(h264_aac, build):
    assert pair_after(build(h264_aac), "-progress") == "pipe:1"


# --- Separacion de pistas ----------------------------------------------------

def test_el_video_no_lleva_audio(h264_aac):
    args = video_args_for(h264_aac)

    assert "-an" in args
    assert pair_after(args, "-map") == "0:v:0"
    assert "-c:a" not in args


def test_el_audio_no_lleva_video(h264_aac):
    args = audio_args_for(h264_aac)

    assert "-vn" in args
    assert pair_after(args, "-map") == "0:a:0"
    assert "-c:v" not in args


@pytest.mark.parametrize("track", [0, 1])
def test_cada_pista_de_audio_se_mapea_por_indice(hevc_ac3, track):
    assert pair_after(audio_args_for(hevc_ac3, track=track), "-map") == f"0:a:{track}"


def test_audio_track_fuera_de_rango(hevc_ac3):
    with pytest.raises(ValueError, match="inexistente"):
        audio_args_for(hevc_ac3, track=9)


@pytest.mark.parametrize("build", [video_args_for, audio_args_for])
def test_descarta_subtitulos_y_capitulos(h264_aac, build):
    args = build(h264_aac)

    assert "-sn" in args
    assert "-dn" in args
    assert pair_after(args, "-map_chapters") == "-1"


# --- Decision de codec -------------------------------------------------------

def test_video_h264_se_copia(h264_aac):
    args = video_args_for(h264_aac)

    assert pair_after(args, "-c:v") == "copy"
    assert "libx264" not in args


def test_video_hevc_se_recodifica_con_cortes_deterministas(hevc_ac3):
    args = video_args_for(hevc_ac3)

    assert pair_after(args, "-c:v") == "libx264"
    assert pair_after(args, "-crf") == "23"
    assert pair_after(args, "-preset") == "veryfast"
    assert pair_after(args, "-pix_fmt") == "yuv420p"
    # Sin keyframes forzados y con cortes de escena las duraciones no serian
    # exactas y la playlist mentiria.
    assert pair_after(args, "-force_key_frames") == "expr:gte(t,n_forced*6)"
    assert pair_after(args, "-sc_threshold") == "0"


def test_audio_aac_estereo_se_copia(h264_aac):
    assert pair_after(audio_args_for(h264_aac), "-c:a") == "copy"


def test_audio_ac3_se_recodifica_a_aac(hevc_ac3):
    args = audio_args_for(hevc_ac3, track=0)

    assert pair_after(args, "-c:a") == "aac"
    assert pair_after(args, "-b:a") == "128k"
    assert pair_after(args, "-ac") == "2"


def test_audio_aac_multicanal_se_downmixea(hevc_ac3):
    info = replace(
        hevc_ac3,
        audio_tracks=(
            AudioTrack(index=0, codec="aac", channels=6, language="spa", title=""),
        ),
    )

    assert pair_after(audio_args_for(info), "-c:a") == "aac"


def test_force_transcode_recodifica_las_dos_pistas(h264_aac):
    assert pair_after(video_args_for(h264_aac, force_transcode=True), "-c:v") == "libx264"
    assert pair_after(audio_args_for(h264_aac, force_transcode=True), "-c:a") == "aac"


# --- Parseo de progreso ------------------------------------------------------

@pytest.mark.parametrize(
    "linea,esperado",
    [
        ("out_time_us=5000000", 5.0),
        ("out_time_ms=5000000", 5.0),
        ("out_time_us=0", 0.0),
        ("frame=120", None),
        ("out_time_us=N/A", None),
        ("basura", None),
    ],
)
def test_parse_progress_line(linea, esperado):
    assert parse_progress_line(linea) == esperado


# --- Ejecucion ---------------------------------------------------------------

async def test_run_ffmpeg_reporta_progreso(spawn_mock):
    process = FakeProcess(
        returncode=0,
        stdout_lines=[b"out_time_us=1000000\n", b"out_time_us=2000000\n"],
    )
    spawn_mock(transcoder, process)

    reported: list[float] = []
    await run_ffmpeg(["-i", "in.mkv"], on_progress=reported.append)

    assert reported == [1.0, 2.0]


async def test_run_ffmpeg_no_duplica_us_y_ms(spawn_mock):
    # FFmpeg emite los dos campos con el mismo valor en cada bloque.
    process = FakeProcess(
        returncode=0,
        stdout_lines=[b"out_time_us=1000000\n", b"out_time_ms=1000000\n"],
    )
    spawn_mock(transcoder, process)

    reported: list[float] = []
    await run_ffmpeg(["-i", "in.mkv"], on_progress=reported.append)

    assert reported == [1.0]


async def test_run_ffmpeg_sin_callback_no_falla(spawn_mock):
    spawn_mock(transcoder, FakeProcess(returncode=0, stdout_lines=[b"out_time_us=1000000\n"]))

    await run_ffmpeg(["-i", "in.mkv"])


async def test_run_ffmpeg_falla_con_stderr(spawn_mock):
    process = FakeProcess(
        returncode=1,
        stderr_lines=[b"Invalid data found\n"],
    )
    spawn_mock(transcoder, process)

    with pytest.raises(FFmpegError) as exc:
        await run_ffmpeg(["-i", "roto.mkv"])

    assert exc.value.returncode == 1
    assert "Invalid data found" in exc.value.stderr


async def test_run_ffmpeg_recorta_el_stderr(spawn_mock):
    process = FakeProcess(
        returncode=1,
        stderr_lines=[f"linea {i}\n".encode() for i in range(200)],
    )
    spawn_mock(transcoder, process)

    with pytest.raises(FFmpegError) as exc:
        await run_ffmpeg(["-i", "roto.mkv"])

    assert len(exc.value.stderr.splitlines()) == transcoder.STDERR_TAIL_LINES


# --- Extraccion de subtitulos -------------------------------------------------

@pytest.mark.parametrize("track", [0, 2])
def test_build_subtitle_args_mapea_pista_y_formato(track):
    args = build_subtitle_args(SOURCE, Path("/out/sub.vtt"), track)

    assert pair_after(args, "-map") == f"0:s:{track}"
    assert pair_after(args, "-c:s") == "webvtt"
    assert args[-1] == str(Path("/out/sub.vtt"))


# --- ManagedFFmpeg ------------------------------------------------------------

async def test_start_ffmpeg_devuelve_handle(spawn_mock):
    process = FakeProcess(returncode=0, stdout_lines=[b"out_time_us=1000000\n"])
    spawn_mock(transcoder, process)

    managed = await start_ffmpeg(["-i", "in.mkv"])
    await managed.wait()

    assert not managed.is_running


async def test_managed_kill_termina_proceso(spawn_mock):
    event = asyncio.Event()

    class BlockingProcess(FakeProcess):
        def __init__(self):
            super().__init__(returncode=0, stdout_lines=[])
            self.returncode = None
            self.terminated = False

        async def wait(self):
            await event.wait()
            self.returncode = 0
            return self.returncode

        def terminate(self):
            self.terminated = True
            event.set()

    process = BlockingProcess()
    spawn_mock(transcoder, process)

    managed = await start_ffmpeg(["-i", "in.mkv"])
    await managed.kill()

    assert process.terminated
    assert not managed.is_running


async def test_managed_killed_no_lanza_error(spawn_mock):
    event = asyncio.Event()

    class BlockingProcess(FakeProcess):
        def __init__(self):
            super().__init__(returncode=1, stdout_lines=[])
            self.returncode = None

        async def wait(self):
            await event.wait()
            self.returncode = 1
            return self.returncode

        def terminate(self):
            event.set()

        def kill(self):
            self.killed = True
            event.set()

    process = BlockingProcess()
    spawn_mock(transcoder, process)

    managed = await start_ffmpeg(["-i", "in.mkv"])
    await managed.kill()
    await managed.wait()
