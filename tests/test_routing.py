"""Tests del espacio de URLs: que sirve cada prefijo y en que orden.

Esto se puede testear porque los mounts se registran al importar el modulo y no
adentro del `lifespan`, que el `ASGITransport` no corre. Mientras vivieron en el
`lifespan`, en los tests no existian.

El bundle del frontend (`static/app`) esta gitignoreado y lo escribe
`npm run build`, asi que lo que depende de que exista se saltea cuando falta. El
orden de resolucion, que es la invariante fragil, se prueba siempre.
"""

from __future__ import annotations

from pathlib import Path

import pytest

SPA = Path("static/app")

sin_bundle = pytest.mark.skipif(
    not (SPA / "index.html").is_file(),
    reason="frontend sin compilar: correr npm run build",
)


@pytest.fixture
async def client():
    """Cliente sin estado: el routing no necesita store ni sesiones."""
    from httpx import ASGITransport, AsyncClient

    from app.main import app

    async with AsyncClient(
        transport=ASGITransport(app=app), base_url="http://test"
    ) as ac:
        yield ac


# --- El frontend --------------------------------------------------------------

@sin_bundle
@pytest.mark.parametrize("path", ["/", "/new/", "/player/"])
async def test_cada_vista_es_un_archivo(client, path):
    """Una pagina por ruta: por eso no hace falta fallback al index.html."""
    resp = await client.get(path)

    assert resp.status_code == 200
    assert resp.headers["content-type"].startswith("text/html")


@sin_bundle
@pytest.mark.parametrize("path", ["/new", "/player"])
async def test_sin_barra_final_redirige(client, path):
    """Starlette manda las URLs de directorio a terminar en "/"."""
    resp = await client.get(path)

    assert resp.status_code == 307
    assert resp.headers["location"].endswith(f"{path}/")


@sin_bundle
async def test_la_query_sobrevive_la_redireccion(client):
    """Los tres modos de entrada del player viajan en la query."""
    resp = await client.get("/player?file=/media/x.mkv")

    assert resp.headers["location"].endswith("/player/?file=/media/x.mkv")


# --- Lo que el frontend no se tiene que comer ---------------------------------

async def test_un_endpoint_inexistente_contesta_el_error_de_la_api(client):
    """El mount de "/" solo acepta GET y HEAD.

    Sin la ruta de /api registrada antes, un POST a un endpoint que no existe
    saldria 405 y un GET con el {"detail": "Not Found"} de Starlette.
    """
    get = await client.get("/api/v1/nope")
    post = await client.post("/api/v1/nope", json={})

    assert get.status_code == 404
    assert get.json()["error"] == "not_found"
    assert post.status_code == 404
    assert post.json()["error"] == "not_found"


async def test_el_schema_le_gana_al_mount(client):
    """`/docs` y `/openapi.json` los registra FastAPI antes de este bloque."""
    resp = await client.get("/openapi.json")

    assert resp.status_code == 200
    assert "/api/v1/media" in resp.json()["paths"]


async def test_el_orden_de_registro(client):
    """El mount de "/" matchea todo, asi que va ultimo.

    Es un fallo silencioso si se invierte: el frontend empezaria a contestar en
    lugar de la API y de las playlists calculadas.
    """
    from app.main import app

    # El mount de "/" tiene `path` vacio: se lo reconoce por su `path_format`.
    paths = [getattr(route, "path_format", None) for route in app.routes]
    mounts = [p for p in paths if p in ("/hls/{path}", "/static/{path}", "/{path}")]

    assert mounts == ["/hls/{path}", "/static/{path}", "/{path}"]
    assert paths.index("/api/{rest}") < paths.index("/{path}")
