"""Repositorio del catalogo. SQLite en memoria, sin ffprobe ni disco."""

from __future__ import annotations

import os
from pathlib import Path

import pytest

from app.manager.entityManager import MEMORY, connect
from app.models.media import Media
from app.repository.media_repository import MediaRepository
from app.services.media_analyzer import SourceInfo


@pytest.fixture
def store() -> MediaRepository:
    # El entityManager abre la conexion; el repositorio solo la usa.
    db = connect(MEMORY)
    yield MediaRepository(db)
    db.close()


def _media(name: str = "Blade Runner 2049.mkv") -> Media:
    path = Path("films") / name  # relativa a MEDIA_ROOT
    return Media(
        id_media=name,
        file_path=str(path),
        file_name=path.name,
        title=path.stem,
    )


def test_alta_y_lectura(store: MediaRepository) -> None:
    store.add(_media())
    ficha = store.get("Blade Runner 2049.mkv")

    assert ficha is not None
    assert ficha.title == "Blade Runner 2049"
    assert ficha.kind == "film"
    assert ficha.in_list is False
    assert ficha.created_at and ficha.updated_at


def test_la_ficha_conserva_lo_que_hace_falta_para_reproducir(
    store: MediaRepository, hevc_ac3: SourceInfo
) -> None:
    """El round-trip por SQLite no puede perder el analisis ni la ruta."""
    original = Media.from_source(Path("films/Dune.mkv"), hevc_ac3, asset_id="abc123")
    store.add(original)

    ficha = store.get(original.id_media)
    assert ficha is not None
    # Relativa: la absoluta se deriva con MEDIA_ROOT y no se persiste.
    assert ficha.file_path == str(Path("films/Dune.mkv"))
    assert ficha.absolute_path(Path("/media")) == Path("/media/films/Dune.mkv")
    assert ficha.asset_id == "abc123"
    assert ficha.duration == 7200.0

    # Y el SourceInfo vuelve entero, sin correr ffprobe de nuevo.
    recuperado = ficha.source_info()
    assert recuperado == hevc_ac3
    assert recuperado.strategy.value == "transcode"


def test_la_misma_ruta_no_entra_dos_veces(store: MediaRepository) -> None:
    store.add(_media())
    assert store.by_path("films/Blade Runner 2049.mkv") is not None

    duplicada = _media()
    duplicada.id_media = "otro-id"
    with pytest.raises(Exception):  # IntegrityError: path_key es UNIQUE
        store.add(duplicada)


@pytest.mark.skipif(os.name != "nt", reason="normcase solo normaliza en Windows")
def test_la_capitalizacion_no_duplica_fichas(store: MediaRepository) -> None:
    store.add(_media())
    assert store.by_path("FILMS/BLADE RUNNER 2049.MKV") is not None


def test_update_solo_toca_lo_editorial(store: MediaRepository) -> None:
    store.add(_media())

    ficha = store.update(
        "Blade Runner 2049.mkv",
        title="Blade Runner 2049",
        year=2017,
        genres=["Sci-fi", "Drama"],
        in_list=True,
        duration=1.0,        # derivado: se ignora
        video_codec="h264",  # derivado: se ignora
    )

    assert ficha is not None
    assert ficha.year == 2017
    assert ficha.genres == ["Sci-fi", "Drama"]
    assert ficha.in_list is True
    assert ficha.duration == 0.0


def test_listado_filtra_ordena_y_cuenta(store: MediaRepository) -> None:
    for name in ("Zodiac.mkv", "Arrival.mkv", "Northern Lines.mkv"):
        store.add(_media(name))
    store.update("Northern Lines.mkv", kind="series", in_list=True)

    titulos = [m.title for m in store.list(limit=10)]
    assert titulos == ["Arrival", "Northern Lines", "Zodiac"]

    assert [m.title for m in store.list(kind="series")] == ["Northern Lines"]
    assert [m.title for m in store.list(in_list=True)] == ["Northern Lines"]
    assert [m.title for m in store.list(q="rriva")] == ["Arrival"]

    assert store.count() == 3
    assert store.count(kind="series") == 1

    # La paginacion no cambia el total: es lo que dibuja "pagina 1 de N".
    assert [m.title for m in store.list(limit=2, offset=2)] == ["Zodiac"]


def test_delete(store: MediaRepository) -> None:
    store.add(_media())
    assert store.delete("Blade Runner 2049.mkv") is True
    assert store.get("Blade Runner 2049.mkv") is None
    assert store.delete("Blade Runner 2049.mkv") is False
