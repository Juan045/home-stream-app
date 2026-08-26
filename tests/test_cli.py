"""Tests de la CLI: parseo de argumentos y traduccion a opciones."""

from __future__ import annotations

from pathlib import Path

import pytest

import transcode
from app.services.transcoder import TranscodeOptions


def test_defaults():
    args = transcode.parse_args(["/media/video.mkv"])

    assert args.source == Path("/media/video.mkv")
    assert args.output == Path("output")
    assert args.serve is False
    assert args.port == 8000
    assert transcode.options_from_args(args) == TranscodeOptions()


def test_flags_llegan_a_las_opciones():
    args = transcode.parse_args([
        "/media/video.mkv",
        "--audio-track", "2",
        "--hls-time", "4",
        "--crf", "20",
        "--preset", "slow",
        "--audio-bitrate", "192k",
        "--audio-channels", "6",
        "--force-transcode",
    ])

    assert transcode.options_from_args(args) == TranscodeOptions(
        audio_track=2, hls_time=4, crf=20, preset="slow",
        audio_bitrate="192k", audio_channels=6, force_transcode=True,
    )


def test_source_es_obligatorio():
    with pytest.raises(SystemExit):
        transcode.parse_args([])


def test_missing_binaries_detecta_faltantes(monkeypatch):
    monkeypatch.setattr(transcode.shutil, "which",
                        lambda b: None if b == "ffmpeg" else "/usr/bin/ffprobe")

    assert transcode.missing_binaries() == ["ffmpeg"]


def test_missing_binaries_vacio_si_estan_todos(monkeypatch):
    monkeypatch.setattr(transcode.shutil, "which", lambda b: f"/usr/bin/{b}")

    assert transcode.missing_binaries() == []


def test_main_sin_ffmpeg_devuelve_error(monkeypatch, capsys):
    import asyncio

    monkeypatch.setattr(transcode.shutil, "which", lambda b: None)

    codigo = asyncio.run(transcode.main(["/media/video.mkv"]))

    assert codigo == 1
    assert "falta(n) en el PATH" in capsys.readouterr().err


def test_main_archivo_inexistente(monkeypatch, capsys, tmp_path):
    import asyncio

    monkeypatch.setattr(transcode.shutil, "which", lambda b: f"/usr/bin/{b}")

    codigo = asyncio.run(transcode.main([str(tmp_path / "no-existe.mkv")]))

    assert codigo == 1
    assert "el archivo no existe" in capsys.readouterr().err


@pytest.mark.parametrize(
    ("segundos", "duracion", "esperado"),
    [
        (30.0, 120.0, "25.0%"),
        (120.0, 120.0, "100.0%"),
        (999.0, 120.0, "100.0%"),   # se topea en 100
    ],
)
def test_progress_printer(capsys, segundos, duracion, esperado):
    transcode.make_progress_printer(duracion, interactive=True)(segundos)

    assert esperado in capsys.readouterr().out


def test_progress_printer_sin_duracion(capsys):
    transcode.make_progress_printer(0.0, interactive=True)(42.0)

    assert "Procesado: 42s" in capsys.readouterr().out


def test_progress_printer_tty_reescribe_una_linea(capsys):
    report = transcode.make_progress_printer(100.0, interactive=True)
    for s in (10.0, 20.0, 30.0):
        report(s)

    salida = capsys.readouterr().out
    assert salida.count("\n") == 0
    assert salida.count("\r") == 3


def test_progress_printer_sin_tty_loguea_por_escalon(capsys):
    """Sin TTY (docker logs) el \\r haria una linea infinita."""
    report = transcode.make_progress_printer(1000.0, interactive=False)
    for s in (5.0, 6.0, 7.0, 20.0, 21.0, 40.0):   # 0.5%, 0.6%, 0.7%, 2%, 2.1%, 4%
        report(s)

    lineas = [l for l in capsys.readouterr().out.splitlines() if l.strip()]
    assert len(lineas) == 3          # un escalon por cada 1% cruzado
    assert "\r" not in "".join(lineas)
