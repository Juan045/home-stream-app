"""Tests de storage: preparacion y medicion del directorio de salida."""

from __future__ import annotations

from app.services.storage import clear_directory, prepare_output_dir, segment_stats


def test_prepare_crea_el_directorio(tmp_path):
    destino = tmp_path / "output"

    prepare_output_dir(destino)

    assert destino.is_dir()


def test_prepare_vacia_lo_anterior(tmp_path):
    destino = tmp_path / "output"
    (destino / "subtitles").mkdir(parents=True)
    (destino / "segment_00000.ts").write_bytes(b"viejo")
    (destino / "subtitles" / "sub.vtt").write_text("WEBVTT")

    prepare_output_dir(destino)

    assert list(destino.iterdir()) == []


def test_prepare_con_keep_conserva_lo_anterior(tmp_path):
    destino = tmp_path / "output"
    destino.mkdir()
    (destino / "segment_00000.ts").write_bytes(b"viejo")

    prepare_output_dir(destino, keep=True)

    assert (destino / "segment_00000.ts").exists()


def test_clear_directory_no_borra_el_directorio(tmp_path):
    """En Docker `output/` es un bind mount: borrarlo daria EBUSY."""
    (tmp_path / "a.ts").write_bytes(b"x")

    clear_directory(tmp_path)

    assert tmp_path.is_dir()
    assert list(tmp_path.iterdir()) == []


def test_segment_stats_cuenta_solo_ts(tmp_path):
    (tmp_path / "segment_00000.ts").write_bytes(b"a" * 100)
    (tmp_path / "segment_00001.ts").write_bytes(b"b" * 400)
    (tmp_path / "master.m3u8").write_text("#EXTM3U")

    stats = segment_stats(tmp_path)

    assert stats.count == 2
    assert stats.total_bytes == 500


def test_segment_stats_directorio_vacio(tmp_path):
    stats = segment_stats(tmp_path)

    assert stats.count == 0
    assert stats.total_mb == 0.0
