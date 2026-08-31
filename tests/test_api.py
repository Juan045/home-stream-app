"""Tests de la API REST y del servido de playlists."""

from __future__ import annotations

from pathlib import Path

import pytest

from app.services.asset_builder import AssetBuilder
from app.services.asset_store import AssetStore
from app.services.session_manager import SessionManager
from app.services.transcoder import TranscodeOptions


@pytest.fixture
def source(tmp_path: Path) -> Path:
    path = tmp_path / "pelicula.mkv"
    path.write_bytes(b"contenido de la pelicula")
    return path


@pytest.fixture
def app(tmp_path: Path, patched, hevc_ac3):
    """App con el estado real, pero con FFmpeg y ffprobe mockeados."""
    from app.main import app as fastapi_app

    patched(info=hevc_ac3)

    store = AssetStore(root=tmp_path / "cache")
    store.root.mkdir(parents=True, exist_ok=True)

    fastapi_app.state.store = store
    fastapi_app.state.sessions = SessionManager()
    fastapi_app.state.builder = AssetBuilder(store=store, options=TranscodeOptions())
    return fastapi_app


@pytest.fixture
async def client(app):
    from httpx import ASGITransport, AsyncClient

    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as ac:
        yield ac


async def open_stream(client, source: Path) -> dict:
    resp = await client.post("/api/v1/stream", json={"file_path": str(source)})
    assert resp.status_code == 201, resp.text
    return resp.json()


# --- Validacion de rutas ----------------------------------------------------

async def test_ruta_relativa_es_rechazada(client):
    resp = await client.post("/api/v1/stream", json={"file_path": "relativa/x.mp4"})

    assert resp.status_code == 400
    assert resp.json() == {
        "error": "invalid_path",
        "detail": "La ruta debe ser absoluta",
    }


async def test_extension_no_soportada(client, tmp_path):
    archivo = tmp_path / "audio.mp3"
    archivo.write_bytes(b"x")

    resp = await client.post("/api/v1/stream", json={"file_path": str(archivo)})

    assert resp.status_code == 400
    assert resp.json()["error"] == "unsupported_extension"


async def test_archivo_inexistente(client, tmp_path):
    resp = await client.post(
        "/api/v1/stream", json={"file_path": str(tmp_path / "fantasma.mkv")}
    )

    assert resp.status_code == 404
    assert resp.json()["error"] == "file_not_found"


async def test_fuera_del_media_root(client, source, monkeypatch):
    from app import config

    monkeypatch.setattr(
        config.get_settings(), "MEDIA_ROOT", source.parent / "otro", raising=False
    )

    resp = await client.post("/api/v1/stream", json={"file_path": str(source)})

    assert resp.status_code == 400
    assert resp.json()["error"] == "outside_media_root"


# --- Apertura de stream -----------------------------------------------------

async def test_post_stream_devuelve_sesion_y_master(client, source):
    data = await open_stream(client, source)

    assert data["session_id"]
    assert data["master_url"] == f"/hls/{data['asset_id']}/master.m3u8"
    assert data["status"] == "ready"
    assert data["strategy"] == "transcode"
    assert data["duration_seconds"] == 7200.0
    assert data["progress"] == 1.0


async def test_post_stream_lista_las_pistas(client, source):
    data = await open_stream(client, source)

    assert [t["language"] for t in data["audio_tracks"]] == ["eng", "spa"]
    assert len(data["subtitle_tracks"]) == 1
    assert data["subtitle_tracks"][0]["url"] == (
        f"/hls/{data['asset_id']}/subs/sub_0_eng.vtt"
    )


async def test_no_hay_endpoints_de_select_ni_seek(client, source):
    # El cambio de audio y el seek son del cliente: si estas rutas reaparecen,
    # es que volvio el diseno de matar y reiniciar FFmpeg.
    data = await open_stream(client, source)
    asset_id = data["asset_id"]

    select = await client.post(f"/api/v1/jobs/{asset_id}/select", json={"audio_track": 1})
    seek = await client.post(f"/api/v1/jobs/{asset_id}/seek", json={"timestamp": 60})

    assert select.status_code == 404
    assert seek.status_code == 404


# --- Sesiones y heartbeat ---------------------------------------------------

async def test_get_session_devuelve_el_progreso(client, source):
    data = await open_stream(client, source)

    resp = await client.get(f"/api/v1/sessions/{data['session_id']}")

    assert resp.status_code == 200
    assert resp.json()["asset_id"] == data["asset_id"]
    assert resp.json()["progress"] == 1.0


async def test_get_session_inexistente(client):
    resp = await client.get("/api/v1/sessions/no-existe")

    assert resp.status_code == 404
    assert resp.json()["error"] == "session_not_found"


async def test_heartbeat_mantiene_viva_la_sesion(client, source, app):
    data = await open_stream(client, source)

    resp = await client.post(f"/api/v1/heartbeat/{data['session_id']}")

    assert resp.status_code == 204
    assert app.state.sessions.is_active(data["session_id"])


async def test_heartbeat_de_sesion_inexistente(client):
    resp = await client.post("/api/v1/heartbeat/no-existe")

    assert resp.status_code == 404
    assert resp.json()["error"] == "session_not_found"


# --- Playlists --------------------------------------------------------------

async def test_master_playlist_declara_las_renditions(client, source):
    data = await open_stream(client, source)

    resp = await client.get(f"/hls/{data['asset_id']}/master.m3u8")

    assert resp.status_code == 200
    assert resp.headers["content-type"].startswith("application/vnd.apple.mpegurl")
    assert resp.text.count("#EXT-X-MEDIA:") == 2
    assert 'AUDIO="aud"' in resp.text


async def test_media_playlists_de_video_y_audio(client, source):
    data = await open_stream(client, source)
    asset_id = data["asset_id"]

    video = await client.get(f"/hls/{asset_id}/video/playlist.m3u8")
    audio_0 = await client.get(f"/hls/{asset_id}/audio/0/playlist.m3u8")
    audio_1 = await client.get(f"/hls/{asset_id}/audio/1/playlist.m3u8")

    assert video.status_code == 200
    assert "#EXT-X-MAP:URI=\"init.mp4\"" in video.text
    # I4: cambiar de idioma no cambia el timeline.
    assert audio_0.text == audio_1.text == video.text


async def test_la_ruta_de_playlist_le_gana_al_mount_estatico(client, source, app):
    # Si el mount de /hls ganara, esto devolveria el internal.m3u8 de FFmpeg en
    # vez de la playlist calculada. Es un fallo silencioso y facil de reintroducir.
    data = await open_stream(client, source)
    asset_id = data["asset_id"]

    internal = app.state.store.paths(asset_id).video / "internal.m3u8"
    internal.write_text("#EXTM3U\n#DELATOR\n", encoding="utf-8")

    resp = await client.get(f"/hls/{asset_id}/video/playlist.m3u8")

    assert "#DELATOR" not in resp.text
    assert "#EXT-X-PLAYLIST-TYPE:" in resp.text


async def test_playlist_de_asset_inexistente(client):
    resp = await client.get("/hls/no-existe/master.m3u8")

    assert resp.status_code == 404
    assert resp.json()["error"] == "playlist_not_found"


async def test_playlist_de_pista_inexistente(client, source):
    data = await open_stream(client, source)

    resp = await client.get(f"/hls/{data['asset_id']}/audio/9/playlist.m3u8")

    assert resp.status_code == 404


# --- Limites ----------------------------------------------------------------

async def test_rechaza_cuando_hay_demasiados_builds(client, source, app, monkeypatch):
    from app import config

    monkeypatch.setattr(config.get_settings(), "MAX_CONCURRENT_FFMPEG", 0, raising=False)

    resp = await client.post("/api/v1/stream", json={"file_path": str(source)})

    assert resp.status_code == 503
    assert resp.json()["error"] == "too_many_jobs"


async def test_rechaza_cuando_el_cache_esta_lleno(client, source, app, monkeypatch):
    from app import config

    monkeypatch.setattr(config.get_settings(), "MAX_CACHE_SIZE", 1, raising=False)
    # Un asset protegido que el GC no puede liberar.
    app.state.store.prepare("ocupado")
    (app.state.store.paths("ocupado").video / "seg-00000.m4s").write_bytes(b"x" * 100)
    app.state.sessions.create("ocupado")

    resp = await client.post("/api/v1/stream", json={"file_path": str(source)})

    assert resp.status_code == 507
    assert resp.json()["error"] == "storage_limit"


async def test_un_asset_ya_abierto_pasa_aunque_este_saturado(
    client, source, monkeypatch,
):
    from app import config

    await open_stream(client, source)
    monkeypatch.setattr(config.get_settings(), "MAX_CONCURRENT_FFMPEG", 0, raising=False)

    resp = await client.post("/api/v1/stream", json={"file_path": str(source)})

    assert resp.status_code == 201
