"""Casos de uso del catalogo. El repositorio real sobre SQLite en memoria.

No hay mock del repositorio a proposito: una BD en memoria es mas rapida que un
doble y no miente sobre lo que SQL acepta. Lo unico parcheado es `analyze`, que
seria el unico que toca un binario.
"""

from __future__ import annotations

from pathlib import Path

import pytest

from app.errors import ApiError, FFmpegError
from app.manager.entityManager import MEMORY, connect
from app.models.media import Media
from app.repository.media_repository import MediaRepository
from app.services import media_service
from app.services.media_analyzer import SourceInfo
from app.services.media_service import MediaService

MEDIA_ROOT = Path("/media")


@pytest.fixture
def service() -> MediaService:
    db = connect(MEMORY)
    yield MediaService(MediaRepository(db), media_root=MEDIA_ROOT)
    db.close()


def _media(name: str) -> Media:
    path = Path("films") / name
    return Media(
        id_media=name, file_path=str(path), file_name=path.name, title=path.stem
    )


async def test_register_guarda_la_ruta_relativa_y_el_analisis(
    tmp_path: Path, monkeypatch, hevc_ac3: SourceInfo
) -> None:
    """El alta traduce la absoluta a relativa y no vuelve a correr ffprobe."""
    source = tmp_path / "films" / "Dune.mkv"
    source.parent.mkdir()
    source.write_bytes(b"no importa el contenido, si el stat")

    async def fake_analyze(path: Path, audio_track: int = 0) -> SourceInfo:
        assert path == source  # ffprobe recibe la absoluta, no la relativa
        return hevc_ac3

    monkeypatch.setattr(media_service, "analyze", fake_analyze)

    db = connect(MEMORY)
    service = MediaService(MediaRepository(db), media_root=tmp_path)
    ficha = await service.register(source)

    assert ficha.file_path == str(Path("films/Dune.mkv"))
    assert ficha.absolute_path(tmp_path) == source
    assert ficha.asset_id  # queda enlazada con su cache HLS
    assert ficha.source_info() == hevc_ac3

    # Y quedo persistida: el chequeo de duplicados la encuentra.
    assert service.find(source) is not None
    db.close()


@pytest.mark.parametrize(
    "fallo",
    [
        FFmpegError("ffprobe", 1, "Invalid data found when processing input"),
        ValueError("El archivo no tiene pistas de video"),
    ],
    ids=["ffprobe_falla", "sin_pista_de_video"],
)
async def test_un_archivo_que_no_es_video_es_400_y_no_500(
    tmp_path: Path, monkeypatch, fallo: Exception
) -> None:
    """La extension no prueba nada: un .mkv vacio pasa la validacion de ruta."""
    source = tmp_path / "roto.mkv"
    source.write_bytes(b"")

    async def fake_analyze(path: Path, audio_track: int = 0) -> SourceInfo:
        raise fallo

    monkeypatch.setattr(media_service, "analyze", fake_analyze)

    db = connect(MEMORY)
    service = MediaService(MediaRepository(db), media_root=tmp_path)

    with pytest.raises(ApiError) as exc:
        await service.register(source)

    assert exc.value.status_code == 400
    assert exc.value.error == "invalid_media"
    assert service.get("roto.mkv") is None  # no quedo nada a medio dar de alta
    db.close()


def test_page_devuelve_la_pagina_y_el_total(service: MediaService) -> None:
    """El total es el contador del encabezado: cuenta todo, no la pagina."""
    for name in ("Zodiac.mkv", "Arrival.mkv", "Northern Lines.mkv"):
        service.add(_media(name))

    items, total = service.page(limit=2)

    assert [m.title for m in items] == ["Arrival", "Northern Lines"]
    assert total == 3


def test_page_aplica_los_mismos_filtros_a_la_lista_y_al_total(
    service: MediaService,
) -> None:
    for name in ("Zodiac.mkv", "Northern Lines.mkv"):
        service.add(_media(name))
    service.update("Northern Lines.mkv", kind="series")

    items, total = service.page(kind="series")

    assert [m.title for m in items] == ["Northern Lines"]
    assert total == 1


def test_find_traduce_la_absoluta_a_relativa(service: MediaService) -> None:
    service.add(_media("Dune.mkv"))

    assert service.find(MEDIA_ROOT / "films" / "Dune.mkv") is not None
    assert service.find(MEDIA_ROOT / "films" / "Otra.mkv") is None


def test_la_baja_pasa_por_el_service(service: MediaService) -> None:
    service.add(_media("Dune.mkv"))

    assert service.delete("Dune.mkv") is True
    assert service.get("Dune.mkv") is None
