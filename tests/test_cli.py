"""Tests de la CLI: argumentos, progreso y artefactos generados."""

from __future__ import annotations

from pathlib import Path

import pytest

import transcode
from transcode import (
    make_progress_printer,
    options_from_args,
    parse_args,
    parse_audio_tracks,
)


@pytest.fixture
def source(tmp_path: Path) -> Path:
    path = tmp_path / "pelicula.mkv"
    path.write_bytes(b"contenido de la pelicula")
    return path


# --- Argumentos --------------------------------------------------------------

def test_defaults(tmp_path):
    args = parse_args(["/media/video.mkv"])

    assert args.source == Path("/media/video.mkv")
    assert args.output == Path("output")
    assert args.hls_time == 6
    assert args.audio_tracks is None
    assert not args.force_transcode
    assert not args.serve


def test_opciones_de_encoding():
    args = parse_args([
        "/media/video.mkv",
        "--crf", "20",
        "--preset", "slow",
        "--audio-bitrate", "192k",
        "--audio-channels", "6",
        "--hls-time", "4",
        "--force-transcode",
    ])
    options = options_from_args(args)

    assert options.crf == 20
    assert options.preset == "slow"
    assert options.audio_bitrate == "192k"
    assert options.audio_channels == 6
    assert options.hls_time == 4
    assert options.force_transcode


def test_output_personalizado():
    assert parse_args(["/v.mkv", "-o", "/tmp/salida"]).output == Path("/tmp/salida")


@pytest.mark.parametrize(
    ("valor", "esperado"),
    [(None, None), ("", None), ("0", {0}), ("0,2", {0, 2}), ("1, 3 ", {1, 3})],
)
def test_parse_audio_tracks(valor, esperado):
    assert parse_audio_tracks(valor) == esperado


def test_parse_audio_tracks_invalido():
    with pytest.raises(SystemExit):
        parse_audio_tracks("uno,dos")


# --- Progreso ----------------------------------------------------------------

def test_el_progreso_sigue_al_video_y_no_al_audio(capsys):
    # El audio corre a ~200x: si contara, la barra saltaria al 100% enseguida.
    report = make_progress_printer(100.0, interactive=False)

    report("audio:0", 90.0)
    report("video", 50.0)

    salida = capsys.readouterr().out
    assert "50.0%" in salida
    assert "90" not in salida


def test_progreso_sin_duracion_conocida(capsys):
    report = make_progress_printer(0.0, interactive=False)

    report("video", 42.0)

    assert "Procesado: 42s" in capsys.readouterr().out


def test_progreso_no_repite_el_mismo_paso(capsys):
    report = make_progress_printer(100.0, interactive=False)

    report("video", 10.0)
    report("video", 10.2)  # mismo 10%

    assert capsys.readouterr().out.count("Progreso") == 1


def test_progreso_interactivo_reescribe_la_linea(capsys):
    report = make_progress_printer(100.0, interactive=True)

    report("video", 50.0)

    assert capsys.readouterr().out.startswith("\r")


# --- Build completo ----------------------------------------------------------

async def test_build_genera_el_layout_completo(patched, hevc_ac3, source, tmp_path):
    spy = patched(info=hevc_ac3)
    args = parse_args([str(source), "-o", str(tmp_path / "cache")])

    asset, master = await transcode.build(args)

    assert master.name == "master.m3u8"
    assert master.exists()
    assert (asset.paths.video / "playlist.m3u8").exists()
    assert (asset.paths.audio(0) / "playlist.m3u8").exists()
    assert (asset.paths.audio(1) / "playlist.m3u8").exists()
    assert (asset.paths.subs / "sub_0_eng.vtt").exists()
    assert len(spy.calls) == 3  # video + dos pistas de audio


async def test_las_playlists_escritas_son_las_calculadas(
    patched, hevc_ac3, source, tmp_path,
):
    patched(info=hevc_ac3)
    args = parse_args([str(source), "-o", str(tmp_path / "cache")])

    asset, master = await transcode.build(args)

    contenido = master.read_text(encoding="utf-8")
    assert contenido.count("#EXT-X-MEDIA:") == 2
    assert contenido.strip().endswith("video/playlist.m3u8")

    video = (asset.paths.video / "playlist.m3u8").read_text(encoding="utf-8")
    audio = (asset.paths.audio(0) / "playlist.m3u8").read_text(encoding="utf-8")
    # I4: el timeline es el mismo para cualquier idioma.
    assert video == audio
    assert "#EXT-X-ENDLIST" in video


async def test_audio_tracks_limita_las_pistas(patched, hevc_ac3, source, tmp_path):
    spy = patched(info=hevc_ac3)
    args = parse_args([str(source), "-o", str(tmp_path / "cache"), "--audio-tracks", "1"])

    asset, master = await transcode.build(args)

    assert set(asset.audio) == {1}
    assert len(spy.calls) == 2  # video + una sola pista
    # El master no declara una rendition que no se genero.
    assert master.read_text(encoding="utf-8").count("#EXT-X-MEDIA:") == 1


async def test_build_fallido_corta_con_error(patched, source, tmp_path):
    patched(fail_on=("0:v:0",))
    args = parse_args([str(source), "-o", str(tmp_path / "cache")])

    with pytest.raises(SystemExit, match="el build fallo"):
        await transcode.build(args)


async def test_clean_vacia_el_cache_antes_de_empezar(patched, source, tmp_path):
    patched()
    cache = tmp_path / "cache"
    cache.mkdir(parents=True)
    (cache / "viejo").mkdir()

    args = parse_args([str(source), "-o", str(cache), "--clean"])
    await transcode.build(args)

    assert not (cache / "viejo").exists()


async def test_reconstruir_usa_el_cache(patched, source, tmp_path):
    patched()
    args = parse_args([str(source), "-o", str(tmp_path / "cache")])
    await transcode.build(args)

    spy = patched()
    await transcode.build(args)

    assert spy.calls == []


# --- Binarios ----------------------------------------------------------------

def test_missing_binaries_detecta_lo_que_falta(monkeypatch):
    monkeypatch.setattr(transcode.shutil, "which", lambda name: None)

    assert transcode.missing_binaries() == ["ffmpeg", "ffprobe"]


def test_missing_binaries_vacio_cuando_estan(monkeypatch):
    monkeypatch.setattr(transcode.shutil, "which", lambda name: f"/usr/bin/{name}")

    assert transcode.missing_binaries() == []
