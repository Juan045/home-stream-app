"""Tests del servidor estatico: URLs. No se abre ningun socket."""

from __future__ import annotations

from pathlib import Path

import pytest

from app.services.static_server import (
    IMMUTABLE_CACHE,
    NO_CACHE,
    cache_control_for,
    manifest_url,
    player_url,
)

ROOT = Path("/app")


@pytest.mark.parametrize(
    ("path", "esperado"),
    [
        ("/output/a1/video/seg-00042.m4s", IMMUTABLE_CACHE),  # inmutable una vez escrito
        ("/output/a1/video/init.mp4", IMMUTABLE_CACHE),
        ("/output/a1/master.m3u8", NO_CACHE),                 # se recalcula
        ("/output/a1/subs/sub_0_spa.vtt", NO_CACHE),
        ("/static/player.html", NO_CACHE),
    ],
)
def test_cache_control_por_tipo(path, esperado):
    assert cache_control_for(path) == esperado


def test_manifest_url_dentro_del_proyecto():
    assert manifest_url(ROOT, ROOT / "output") == "/output/master.m3u8"


def test_manifest_url_anidado():
    assert manifest_url(ROOT, ROOT / "output" / "job-1") == "/output/job-1/master.m3u8"


def test_manifest_url_fuera_del_proyecto_es_none():
    assert manifest_url(ROOT, Path("/var/tmp/output")) is None


def test_player_url_incluye_el_manifest():
    url = player_url(8080, "/output/master.m3u8")

    assert url == "http://localhost:8080/static/player.html?src=/output/master.m3u8"
