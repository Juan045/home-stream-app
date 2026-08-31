"""Tests de asset_store: identidad, layout, manifest y GC."""

from __future__ import annotations

from pathlib import Path

import pytest

from app.services.asset_store import ASSET_ID_LENGTH, AssetStore, asset_id_for


@pytest.fixture
def store(tmp_path: Path) -> AssetStore:
    return AssetStore(root=tmp_path / "cache")


@pytest.fixture
def source(tmp_path: Path) -> Path:
    path = tmp_path / "pelicula.mkv"
    path.write_bytes(b"contenido")
    return path


def fill(directory: Path, name: str, size: int) -> None:
    directory.mkdir(parents=True, exist_ok=True)
    (directory / name).write_bytes(b"x" * size)


# --- Identidad --------------------------------------------------------------

def test_asset_id_es_estable(source: Path):
    assert asset_id_for(source) == asset_id_for(source)
    assert len(asset_id_for(source)) == ASSET_ID_LENGTH


def test_asset_id_cambia_si_cambia_el_contenido(source: Path):
    original = asset_id_for(source)
    source.write_bytes(b"otro contenido mas largo")

    assert asset_id_for(source) != original


def test_asset_id_cambia_si_cambia_el_mtime(source: Path):
    original = asset_id_for(source)
    stat = source.stat()
    import os

    os.utime(source, ns=(stat.st_atime_ns, stat.st_mtime_ns + 1_000_000_000))

    assert asset_id_for(source) != original


def test_asset_id_distingue_archivos_distintos(tmp_path: Path, source: Path):
    otro = tmp_path / "otra.mkv"
    otro.write_bytes(b"contenido")

    assert asset_id_for(otro) != asset_id_for(source)


def test_locate_devuelve_id_y_rutas(store: AssetStore, source: Path):
    asset_id, paths = store.locate(source)

    assert paths.root == store.root / asset_id
    assert paths.video == paths.root / "video"
    assert paths.subs == paths.root / "subs"
    assert paths.audio(1) == paths.root / "audio" / "1"


# --- Layout -----------------------------------------------------------------

def test_prepare_crea_el_arbol(store: AssetStore):
    paths = store.prepare("abc123")

    assert paths.video.is_dir()
    assert paths.subs.is_dir()
    assert (paths.root / "audio").is_dir()


def test_prepare_es_idempotente(store: AssetStore):
    store.prepare("abc123")
    store.prepare("abc123")

    assert store.paths("abc123").video.is_dir()


def test_prepare_audio_crea_el_directorio_de_la_pista(store: AssetStore):
    store.prepare("abc123")

    assert store.prepare_audio("abc123", 2).is_dir()


# --- Manifest ---------------------------------------------------------------

def test_manifest_round_trip(store: AssetStore):
    store.prepare("abc123")
    data = {"duration": 120.5, "video": "ready", "titulo": "Espanol - Comentario"}

    store.write_manifest("abc123", data)

    assert store.read_manifest("abc123") == data
    assert store.exists("abc123")


def test_manifest_inexistente_devuelve_none(store: AssetStore):
    assert store.read_manifest("no-existe") is None
    assert not store.exists("no-existe")


def test_manifest_corrupto_devuelve_none(store: AssetStore):
    paths = store.prepare("abc123")
    paths.manifest.write_text("{ esto no es json", encoding="utf-8")

    assert store.read_manifest("abc123") is None


def test_write_manifest_no_deja_temporales(store: AssetStore):
    store.prepare("abc123")
    store.write_manifest("abc123", {"a": 1})

    assert list(store.paths("abc123").root.glob("*.tmp")) == []


# --- Uso reciente y espacio -------------------------------------------------

def test_touch_registra_el_ultimo_uso(store: AssetStore):
    store.prepare("abc123")
    store.touch("abc123", when=1000.0)

    assert store.last_access("abc123") == 1000.0


def test_last_access_sin_marca_es_cero(store: AssetStore):
    store.prepare("abc123")
    (store.paths("abc123").root / ".last-access").unlink()

    assert store.last_access("abc123") == 0.0


def test_size_y_total_bytes(store: AssetStore):
    store.prepare("uno")
    store.prepare("dos")
    fill(store.paths("uno").video, "seg-00000.m4s", 500)
    fill(store.paths("dos").video, "seg-00000.m4s", 300)

    assert store.size_bytes("uno") >= 500
    assert store.total_bytes() >= 800


def test_asset_ids_lista_los_directorios(store: AssetStore):
    store.prepare("uno")
    store.prepare("dos")

    assert store.asset_ids() == ["dos", "uno"]


# --- GC ---------------------------------------------------------------------

def test_collect_borra_el_menos_usado_primero(store: AssetStore):
    for name, when in (("viejo", 100.0), ("medio", 200.0), ("nuevo", 300.0)):
        store.prepare(name)
        fill(store.paths(name).video, "seg-00000.m4s", 1000)
        store.touch(name, when=when)

    removed = store.collect(max_bytes=2500)

    assert removed == ["viejo"]
    assert set(store.asset_ids()) == {"medio", "nuevo"}


def test_collect_sigue_borrando_hasta_bajar_del_tope(store: AssetStore):
    for name, when in (("a", 100.0), ("b", 200.0), ("c", 300.0)):
        store.prepare(name)
        fill(store.paths(name).video, "seg-00000.m4s", 1000)
        store.touch(name, when=when)

    removed = store.collect(max_bytes=1200)

    assert removed == ["a", "b"]
    assert store.asset_ids() == ["c"]


def test_collect_no_toca_los_protegidos(store: AssetStore):
    # Un asset con sesion activa no se borra aunque sea el mas viejo: es
    # preferible pasarse del tope a cortarle la reproduccion a alguien.
    for name, when in (("viejo", 100.0), ("nuevo", 300.0)):
        store.prepare(name)
        fill(store.paths(name).video, "seg-00000.m4s", 1000)
        store.touch(name, when=when)

    removed = store.collect(max_bytes=500, keep={"viejo"})

    assert removed == ["nuevo"]
    assert store.asset_ids() == ["viejo"]


def test_collect_no_borra_nada_si_hay_espacio(store: AssetStore):
    store.prepare("uno")
    fill(store.paths("uno").video, "seg-00000.m4s", 100)

    assert store.collect(max_bytes=10_000) == []
    assert store.asset_ids() == ["uno"]


def test_remove_de_un_asset_inexistente_no_falla(store: AssetStore):
    assert store.remove("no-existe") is True


def test_clear_vacia_el_cache_sin_borrar_la_raiz(store: AssetStore):
    store.prepare("uno")
    store.prepare("dos")

    store.clear()

    assert store.root.is_dir()
    assert store.asset_ids() == []
