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


async def open_stream(client, app, source: Path) -> dict:
    """Abre el stream y espera a que los builds terminen.

    `open()` ya no bloquea, asi que el POST vuelve con el asset a medio hacer;
    los tests que miran el resultado final tienen que esperar.
    """
    resp = await client.post("/api/v1/stream", json={"file_path": str(source)})
    assert resp.status_code == 201, resp.text

    await app.state.builder.wait_for_builds()
    session_id = resp.json()["session_id"]
    return (await client.get(f"/api/v1/sessions/{session_id}")).json()


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

async def test_post_stream_devuelve_sesion_y_master(client, app, source):
    data = await open_stream(client, app, source)

    assert data["session_id"]
    assert data["master_url"] == f"/hls/{data['asset_id']}/master.m3u8"
    assert data["status"] == "ready"
    assert data["strategy"] == "transcode"
    assert data["duration_seconds"] == 7200.0
    assert data["progress"] == 1.0


async def test_post_stream_lista_las_pistas(client, app, source):
    data = await open_stream(client, app, source)

    assert [t["language"] for t in data["audio_tracks"]] == ["eng", "spa"]
    assert len(data["subtitle_tracks"]) == 1
    assert data["subtitle_tracks"][0]["url"] == (
        f"/hls/{data['asset_id']}/subs/sub_0_eng.vtt"
    )


async def test_no_hay_endpoints_de_select_ni_seek(client, app, source):
    # El cambio de audio y el seek son del cliente: si estas rutas reaparecen,
    # es que volvio el diseno de matar y reiniciar FFmpeg.
    data = await open_stream(client, app, source)
    asset_id = data["asset_id"]

    select = await client.post(f"/api/v1/jobs/{asset_id}/select", json={"audio_track": 1})
    seek = await client.post(f"/api/v1/jobs/{asset_id}/seek", json={"timestamp": 60})

    assert select.status_code == 404
    assert seek.status_code == 404


# --- Sesiones y heartbeat ---------------------------------------------------

async def test_get_session_devuelve_el_progreso(client, app, source):
    data = await open_stream(client, app, source)

    resp = await client.get(f"/api/v1/sessions/{data['session_id']}")

    assert resp.status_code == 200
    assert resp.json()["asset_id"] == data["asset_id"]
    assert resp.json()["progress"] == 1.0


async def test_get_session_inexistente(client):
    resp = await client.get("/api/v1/sessions/no-existe")

    assert resp.status_code == 404
    assert resp.json()["error"] == "session_not_found"


async def test_heartbeat_mantiene_viva_la_sesion(client, source, app):
    data = await open_stream(client, app, source)

    resp = await client.post(f"/api/v1/heartbeat/{data['session_id']}")

    assert resp.status_code == 204
    assert app.state.sessions.is_active(data["session_id"])


async def test_heartbeat_de_sesion_inexistente(client):
    resp = await client.post("/api/v1/heartbeat/no-existe")

    assert resp.status_code == 404
    assert resp.json()["error"] == "session_not_found"


# --- Playlists --------------------------------------------------------------

async def test_master_playlist_declara_las_renditions(client, app, source):
    data = await open_stream(client, app, source)

    resp = await client.get(f"/hls/{data['asset_id']}/master.m3u8")

    assert resp.status_code == 200
    assert resp.headers["content-type"].startswith("application/vnd.apple.mpegurl")
    assert resp.text.count("#EXT-X-MEDIA:") == 2
    assert 'AUDIO="aud"' in resp.text


async def test_media_playlists_de_video_y_audio(client, app, source):
    data = await open_stream(client, app, source)
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
    data = await open_stream(client, app, source)
    asset_id = data["asset_id"]

    internal = app.state.store.paths(asset_id).video / "internal.m3u8"
    internal.write_text("#EXTM3U\n#DELATOR\n", encoding="utf-8")

    resp = await client.get(f"/hls/{asset_id}/video/playlist.m3u8")

    assert "#DELATOR" not in resp.text
    assert "#EXT-X-PLAYLIST-TYPE:" in resp.text


async def test_playlist_de_asset_inexistente(client):
    resp = await client.get("/hls/no-existe/master.m3u8")

    assert resp.status_code == 404
    assert resp.json()["error"] == "asset_not_found"


async def test_playlist_todavia_no_escrita_responde_503(client, app, source):
    # Un asset en construccion no es un 404: eso le dice a hls.js que deje de
    # pedir la playlist, y efectivamente abandona despues de unos reintentos.
    data = await open_stream(client, app, source)
    (app.state.store.paths(data["asset_id"]).video / "internal.m3u8").unlink()

    resp = await client.get(f"/hls/{data['asset_id']}/video/playlist.m3u8")

    assert resp.status_code == 503
    assert resp.json()["error"] == "playlist_not_ready"
    assert resp.headers["retry-after"] == "2"


async def test_playable_indica_si_se_puede_empezar(client, app, source):
    # El player consulta este campo antes de pedir el video.
    resp = await client.post("/api/v1/stream", json={"file_path": str(source)})
    assert "playable" in resp.json()

    data = await open_stream(client, app, source)
    assert data["playable"] is True


async def test_playlist_de_pista_inexistente(client, app, source):
    data = await open_stream(client, app, source)

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
    client, app, source, monkeypatch,
):
    from app import config

    await open_stream(client, app, source)
    monkeypatch.setattr(config.get_settings(), "MAX_CONCURRENT_FFMPEG", 0, raising=False)

    resp = await client.post("/api/v1/stream", json={"file_path": str(source)})

    assert resp.status_code == 201


# --- ABM: alta de medios ----------------------------------------------------

@pytest.fixture
def abm(app, tmp_path, monkeypatch, hevc_ac3):
    """App con el ABM enchufado: BD en memoria y MEDIA_ROOT propio.

    No toca la fixture `app` compartida mas alla del estado del catalogo, asi
    que los tests de /stream siguen viendo lo mismo de antes.
    """
    from app import config
    from app.manager.entityManager import MEMORY, connect
    from app.repository.media_repository import MediaRepository
    from app.services import media_service
    from app.services.media_service import MediaService

    media_root = tmp_path / "media"
    (media_root / "films").mkdir(parents=True)
    monkeypatch.setattr(config.get_settings(), "MEDIA_ROOT", media_root, raising=False)

    # El ABM tiene su propio llamador de analyze: la fixture `patched` solo
    # cubre el de asset_builder.
    async def fake_analyze(path, audio_track: int = 0):
        return hevc_ac3

    monkeypatch.setattr(media_service, "analyze", fake_analyze)

    db = connect(MEMORY)
    app.state.media = MediaService(
        MediaRepository(db), media_root=media_root.resolve()
    )
    yield media_root
    app.state.media = None
    db.close()


@pytest.fixture
def pelicula(abm: Path) -> Path:
    path = abm / "films" / "Dune.mkv"
    path.write_bytes(b"contenido de la pelicula")
    return path


async def test_alta_de_un_medio(client, pelicula):
    resp = await client.post("/api/v1/media", json={"file_path": "films/Dune.mkv"})

    assert resp.status_code == 201, resp.text
    body = resp.json()

    # Se guarda la relativa, no la absoluta del contenedor.
    assert body["file_path"] == str(Path("films/Dune.mkv"))
    assert body["file_name"] == "Dune.mkv"
    assert body["title"] == "Dune"
    # Y el asset_id la enlaza con su cache HLS: es lo que permite reproducirla.
    assert body["asset_id"]
    assert resp.headers["location"] == f"/api/v1/media/{body['id_media']}"


async def test_el_alta_devuelve_los_derivados_aplanados(client, pelicula):
    resp = await client.post("/api/v1/media", json={"file_path": "films/Dune.mkv"})
    body = resp.json()

    assert "info" not in body
    assert body["video_codec"] == "hevc"
    assert body["height"] == 2160
    assert body["strategy"] == "transcode"
    assert [t["language"] for t in body["audio_tracks"]] == ["eng", "spa"]
    assert [t["language"] for t in body["subtitle_tracks"]] == ["eng"]


async def test_el_mismo_archivo_dos_veces_es_409(client, pelicula):
    primera = await client.post("/api/v1/media", json={"file_path": "films/Dune.mkv"})
    segunda = await client.post("/api/v1/media", json={"file_path": "films/Dune.mkv"})

    assert segunda.status_code == 409
    assert segunda.json()["error"] == "media_already_exists"
    # El id de la ficha que ya estaba, para que el formulario pueda ir a ella.
    assert primera.json()["id_media"] in segunda.json()["detail"]


async def test_la_ruta_absoluta_es_rechazada(client, pelicula):
    resp = await client.post("/api/v1/media", json={"file_path": str(pelicula)})

    assert resp.status_code == 400
    assert resp.json() == {
        "error": "invalid_path",
        "detail": "La ruta debe ser relativa a MEDIA_ROOT",
    }


async def test_no_se_puede_salir_de_media_root(client, abm, tmp_path):
    afuera = tmp_path / "secreto.mkv"
    afuera.write_bytes(b"x")

    resp = await client.post("/api/v1/media", json={"file_path": "../secreto.mkv"})

    assert resp.status_code == 400
    assert resp.json()["error"] == "outside_media_root"


async def test_archivo_inexistente_es_404(client, abm):
    resp = await client.post("/api/v1/media", json={"file_path": "films/nada.mkv"})

    assert resp.status_code == 404
    assert resp.json()["error"] == "file_not_found"


async def test_un_archivo_que_no_es_video_es_400(client, pelicula, monkeypatch):
    from app.errors import FFmpegError
    from app.services import media_service

    async def falla(path, audio_track: int = 0):
        raise FFmpegError("ffprobe", 1, "Invalid data found when processing input")

    monkeypatch.setattr(media_service, "analyze", falla)

    resp = await client.post("/api/v1/media", json={"file_path": "films/Dune.mkv"})

    assert resp.status_code == 400
    assert resp.json()["error"] == "invalid_media"


async def test_sin_media_root_el_abm_no_esta_disponible(client, app):
    app.state.media = None

    resp = await client.post("/api/v1/media", json={"file_path": "films/Dune.mkv"})

    assert resp.status_code == 500
    assert resp.json()["error"] == "media_root_not_configured"


async def test_la_ficha_y_el_stream_apuntan_al_mismo_asset(client, pelicula):
    """Es lo que hace que una pelicula recien cargada se pueda reproducir."""
    alta = await client.post("/api/v1/media", json={"file_path": "films/Dune.mkv"})
    stream = await client.post("/api/v1/stream", json={"file_path": str(pelicula)})

    assert stream.status_code == 201, stream.text
    assert alta.json()["asset_id"] == stream.json()["asset_id"]


async def test_detalle_de_una_ficha(client, pelicula):
    creada = (
        await client.post("/api/v1/media", json={"file_path": "films/Dune.mkv"})
    ).json()

    resp = await client.get(f"/api/v1/media/{creada['id_media']}")

    assert resp.status_code == 200, resp.text
    # Misma forma que devolvio el alta: el mapper es el mismo.
    assert resp.json() == creada


async def test_el_detalle_no_toca_el_disco(client, pelicula):
    """La ficha sobrevive a que el archivo desaparezca: sale toda de la BD."""
    creada = (
        await client.post("/api/v1/media", json={"file_path": "films/Dune.mkv"})
    ).json()
    pelicula.unlink()

    resp = await client.get(f"/api/v1/media/{creada['id_media']}")

    assert resp.status_code == 200
    assert resp.json()["title"] == "Dune"


async def test_una_ficha_inexistente_es_404(client, abm):
    resp = await client.get("/api/v1/media/no-existe")

    assert resp.status_code == 404
    assert resp.json() == {
        "error": "media_not_found",
        "detail": "No existe una ficha con id no-existe",
    }


# --- ABM: listado -----------------------------------------------------------

async def alta(client, abm: Path, nombre: str) -> dict:
    """Da de alta una pelicula creando el archivo que la respalda."""
    (abm / "films" / nombre).write_bytes(b"x")
    resp = await client.post("/api/v1/media", json={"file_path": f"films/{nombre}"})
    assert resp.status_code == 201, resp.text
    return resp.json()


async def test_listado_vacio_es_200(client, abm):
    """No hay resultados es una respuesta exitosa, no un 404."""
    resp = await client.get("/api/v1/media")

    assert resp.status_code == 200
    assert resp.json() == {"items": [], "total": 0, "limit": 10, "offset": 0}


async def test_listado_ordenado_por_titulo(client, abm):
    for nombre in ("Zodiac.mkv", "Arrival.mkv", "Dune.mkv"):
        await alta(client, abm, nombre)

    body = (await client.get("/api/v1/media")).json()

    assert [i["title"] for i in body["items"]] == ["Arrival", "Dune", "Zodiac"]
    assert body["total"] == 3


async def test_cada_fila_trae_solo_lo_que_dibuja_una_tarjeta(client, abm):
    await alta(client, abm, "Dune.mkv")

    item = (await client.get("/api/v1/media")).json()["items"][0]

    assert set(item) == {"id_media", "title", "kind", "year", "duration"}
    assert item["kind"] == "film"
    assert item["duration"] == 7200.0
    # El detalle (sinopsis, pistas, resolucion) lo trae GET /media/{id}.
    assert (await client.get(f"/api/v1/media/{item['id_media']}")).status_code == 200


async def test_la_paginacion_no_cambia_el_total(client, abm):
    for nombre in ("Zodiac.mkv", "Arrival.mkv", "Dune.mkv"):
        await alta(client, abm, nombre)

    body = (await client.get("/api/v1/media?limit=2&offset=2")).json()

    assert [i["title"] for i in body["items"]] == ["Zodiac"]
    assert body["total"] == 3  # el contador de la seccion, no el de la pagina
    assert body["limit"] == 2
    assert body["offset"] == 2


async def test_filtros_del_listado(client, abm, app):
    await alta(client, abm, "Arrival.mkv")
    serie = await alta(client, abm, "Northern Lines.mkv")
    app.state.media.update(serie["id_media"], kind="series", in_list=True)

    por_kind = (await client.get("/api/v1/media?kind=series")).json()
    por_lista = (await client.get("/api/v1/media?in_list=true")).json()
    por_texto = (await client.get("/api/v1/media?q=rriva")).json()

    assert [i["title"] for i in por_kind["items"]] == ["Northern Lines"]
    assert por_kind["total"] == 1
    assert [i["title"] for i in por_lista["items"]] == ["Northern Lines"]
    assert [i["title"] for i in por_texto["items"]] == ["Arrival"]


async def test_sort_por_agregado_reciente(client, abm):
    await alta(client, abm, "Arrival.mkv")
    await alta(client, abm, "Zodiac.mkv")

    body = (await client.get("/api/v1/media?sort=added")).json()

    assert [i["title"] for i in body["items"]] == ["Zodiac", "Arrival"]


async def test_paginacion_invalida_es_422(client, abm):
    assert (await client.get("/api/v1/media?limit=999")).status_code == 422
    assert (await client.get("/api/v1/media?offset=-1")).status_code == 422
    assert (await client.get("/api/v1/media?sort=progress")).status_code == 422


# --- ABM: edicion -----------------------------------------------------------

async def test_edita_los_campos_editoriales(client, abm):
    ficha = await alta(client, abm, "Arrival.mkv")

    resp = await client.patch(
        f"/api/v1/media/{ficha['id_media']}",
        json={
            "title": "Arrival",
            "year": 2016,
            "kind": "film",
            "genres": ["Sci-fi", "Drama"],
            "notes": "Version del director",
            "in_list": True,
        },
    )

    assert resp.status_code == 200, resp.text
    body = resp.json()
    assert body["title"] == "Arrival"
    assert body["year"] == 2016
    assert body["genres"] == ["Sci-fi", "Drama"]
    assert body["in_list"] is True
    # Y devuelve la ficha entera, no un 204: el formulario se repinta sin otro GET.
    assert body["video_codec"] == "hevc"


async def test_lo_que_no_se_manda_no_se_toca(client, pelicula):
    creada = (
        await client.post("/api/v1/media", json={"file_path": "films/Dune.mkv"})
    ).json()
    await client.patch(
        f"/api/v1/media/{creada['id_media']}",
        json={"year": 2021, "synopsis": "Paul Atreides llega a Arrakis"},
    )

    # Un segundo submit que solo toca el titulo no puede vaciar lo anterior.
    body = (
        await client.patch(
            f"/api/v1/media/{creada['id_media']}", json={"title": "Dune"}
        )
    ).json()

    assert body["title"] == "Dune"
    assert body["year"] == 2021
    assert body["synopsis"] == "Paul Atreides llega a Arrakis"


async def test_un_null_explicito_vacia_el_campo(client, pelicula):
    creada = (
        await client.post("/api/v1/media", json={"file_path": "films/Dune.mkv"})
    ).json()
    await client.patch(f"/api/v1/media/{creada['id_media']}", json={"year": 1984})

    body = (
        await client.patch(
            f"/api/v1/media/{creada['id_media']}", json={"year": None}
        )
    ).json()

    assert body["year"] is None


async def test_un_body_vacio_devuelve_la_ficha_sin_cambios(client, pelicula):
    creada = (
        await client.post("/api/v1/media", json={"file_path": "films/Dune.mkv"})
    ).json()

    resp = await client.patch(f"/api/v1/media/{creada['id_media']}", json={})

    assert resp.status_code == 200
    assert resp.json()["title"] == creada["title"]


async def test_no_se_puede_editar_un_campo_derivado(client, pelicula):
    creada = (
        await client.post("/api/v1/media", json={"file_path": "films/Dune.mkv"})
    ).json()

    resp = await client.patch(
        f"/api/v1/media/{creada['id_media']}", json={"video_codec": "h264"}
    )

    assert resp.status_code == 422


async def test_los_campos_not_null_no_se_pueden_vaciar(client, pelicula):
    creada = (
        await client.post("/api/v1/media", json={"file_path": "films/Dune.mkv"})
    ).json()

    for campo in ("title", "kind", "in_list"):
        resp = await client.patch(
            f"/api/v1/media/{creada['id_media']}", json={campo: None}
        )
        assert resp.status_code == 422, campo


async def test_editar_una_ficha_inexistente_es_404(client, abm):
    resp = await client.patch("/api/v1/media/no-existe", json={"title": "X"})

    assert resp.status_code == 404
    assert resp.json()["error"] == "media_not_found"


# --- Reproducir desde el catalogo -------------------------------------------
#
# `POST /stream` acepta `id_media` o `file_path`. La galeria usa el primero: la
# ficha guarda la ruta relativa a MEDIA_ROOT y el cliente no conoce el punto de
# montaje del servidor.

async def alta_de_la_pelicula(client) -> dict:
    """El alta de la fixture `pelicula`, que ya escribio el archivo."""
    resp = await client.post("/api/v1/media", json={"file_path": "films/Dune.mkv"})
    assert resp.status_code == 201, resp.text
    return resp.json()


async def test_abrir_un_stream_por_id_media(client, pelicula):
    ficha = await alta_de_la_pelicula(client)

    resp = await client.post("/api/v1/stream", json={"id_media": ficha["id_media"]})

    assert resp.status_code == 201, resp.text
    assert resp.json()["session_id"]


async def test_el_asset_es_el_mismo_por_las_dos_vias(client, pelicula):
    """El `asset_id` sale de la terna (ruta, mtime, tamano), no de como se pidio.

    Es lo que hace que reproducir desde la galeria reuse el cache de una apertura
    manual en vez de volver a codificar.
    """
    ficha = await alta_de_la_pelicula(client)

    por_id = await client.post("/api/v1/stream", json={"id_media": ficha["id_media"]})
    por_ruta = await client.post("/api/v1/stream", json={"file_path": str(pelicula)})

    assert por_id.json()["asset_id"] == por_ruta.json()["asset_id"]
    # Pero cada apertura es un espectador distinto.
    assert por_id.json()["session_id"] != por_ruta.json()["session_id"]


async def test_el_asset_id_coincide_con_el_de_la_ficha(client, pelicula):
    ficha = await alta_de_la_pelicula(client)

    resp = await client.post("/api/v1/stream", json={"id_media": ficha["id_media"]})

    assert resp.json()["asset_id"] == ficha["asset_id"]


async def test_una_ficha_inexistente_es_404(client, abm):
    resp = await client.post("/api/v1/stream", json={"id_media": "no-existe"})

    assert resp.status_code == 404
    assert resp.json()["error"] == "media_not_found"


async def test_si_el_archivo_se_borro_despues_del_alta_es_404(client, pelicula):
    """La ficha dice donde *estaba* el archivo, no que siga estando.

    Resolver por `id_media` no saltea `validate_path`: es el mismo camino.
    """
    ficha = await alta_de_la_pelicula(client)
    pelicula.unlink()

    resp = await client.post("/api/v1/stream", json={"id_media": ficha["id_media"]})

    assert resp.status_code == 404
    assert resp.json()["error"] == "file_not_found"


@pytest.mark.parametrize(
    "body",
    [
        {},
        {"file_path": None, "id_media": None},
        {"file_path": "/media/films/Dune.mkv", "id_media": "abc"},
    ],
    ids=["vacio", "los-dos-en-null", "los-dos-cargados"],
)
async def test_hay_que_mandar_exactamente_uno(client, body):
    """Mandar los dos obligaria a elegir cual gana cuando no coinciden.

    Esa ambiguedad la resuelve quien llama, no el servidor.
    """
    resp = await client.post("/api/v1/stream", json=body)

    assert resp.status_code == 422


async def test_sin_media_root_no_se_puede_reproducir_por_id(client, app):
    app.state.media = None

    resp = await client.post("/api/v1/stream", json={"id_media": "cualquiera"})

    assert resp.status_code == 500
    assert resp.json()["error"] == "media_root_not_configured"
