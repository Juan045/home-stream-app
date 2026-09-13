"""Tests del servidor estatico del modo CLI. No se abre ningun socket."""

from __future__ import annotations

from pathlib import Path

import pytest

from app.services.static_server import (
    IMMUTABLE_CACHE,
    NO_CACHE,
    QuietHandler,
    cache_control_for,
    mounted_url,
)


@pytest.mark.parametrize(
    ("path", "esperado"),
    [
        ("/output/a1/video/seg-00042.m4s", IMMUTABLE_CACHE),  # inmutable una vez escrito
        ("/output/a1/video/init.mp4", IMMUTABLE_CACHE),
        ("/output/a1/master.m3u8", NO_CACHE),                 # se recalcula
        ("/output/a1/subs/sub_0_spa.vtt", NO_CACHE),
        ("/player/index.html", NO_CACHE),
    ],
)
def test_cache_control_por_tipo(path, esperado):
    assert cache_control_for(path) == esperado


def test_mounted_url_cuelga_del_prefijo(tmp_path):
    master = tmp_path / "a1" / "master.m3u8"
    master.parent.mkdir()
    master.touch()

    assert mounted_url("/output", tmp_path, master) == "/output/a1/master.m3u8"


def test_mounted_url_no_duplica_la_barra(tmp_path):
    master = tmp_path / "master.m3u8"
    master.touch()

    assert mounted_url("/output/", tmp_path, master) == "/output/master.m3u8"


# --- translate_path ---------------------------------------------------------
#
# El handler se construye por request y su __init__ habla un socket, asi que se
# lo saltea: `translate_path` solo necesita `extra` y el directorio raiz.

def resolver(spa: Path, extra: dict[str, Path], path: str) -> str:
    handler = QuietHandler.__new__(QuietHandler)
    handler.extra = extra
    handler.directory = str(spa)
    return handler.translate_path(path)


def test_lo_que_no_matchea_un_prefijo_sale_de_la_raiz(tmp_path):
    spa, output = tmp_path / "app", tmp_path / "output"
    spa.mkdir()
    output.mkdir()

    resuelto = resolver(spa, {"/output": output}, "/player/index.html")

    assert Path(resuelto) == spa / "player" / "index.html"


def test_el_prefijo_manda_sobre_la_raiz(tmp_path):
    spa, output = tmp_path / "app", tmp_path / "output"
    spa.mkdir()
    output.mkdir()

    resuelto = resolver(spa, {"/output": output}, "/output/a1/master.m3u8")

    assert Path(resuelto) == output / "a1" / "master.m3u8"


def test_el_prefijo_pelado_es_el_directorio(tmp_path):
    output = tmp_path / "output"
    output.mkdir()

    assert Path(resolver(tmp_path, {"/output": output}, "/output")) == output


def test_un_prefijo_que_solo_comparte_texto_no_matchea(tmp_path):
    """`/outputs` no cuelga de `/output`: el corte es por segmento."""
    spa, output = tmp_path / "app", tmp_path / "output"
    spa.mkdir()
    output.mkdir()

    resuelto = resolver(spa, {"/output": output}, "/outputs/x.txt")

    assert Path(resuelto) == spa / "outputs" / "x.txt"


@pytest.mark.parametrize(
    "path",
    [
        "/output/../../etc/passwd",
        "/output/a1/../../../etc/passwd",
        "/output/%2e%2e/%2e%2e/etc/passwd",  # el mismo escape, percent-encoded
    ],
)
def test_el_traversal_no_sale_del_directorio_montado(tmp_path, path):
    """Lo que sigue al prefijo lo escribe quien pide: tiene que quedar adentro.

    Devolver el directorio hace que el handler conteste 404, que es lo que
    corresponde a una ruta que no existe.
    """
    output = tmp_path / "output"
    output.mkdir()

    assert Path(resolver(tmp_path, {"/output": output}, path)) == output
