"""Servidor de archivos estaticos del modo CLI.

Sirve un directorio raiz y, opcionalmente, otros montados bajo un prefijo: el
frontend compilado vive en `static/app` y el directorio de salida en cualquier
parte, y `SimpleHTTPRequestHandler` sirve uno solo.

No sabe que es un player: quien lo levanta decide que URL anunciar.
"""

from __future__ import annotations

import functools
import http.server
import mimetypes
import socketserver
import sys
import threading
import urllib.parse
from pathlib import Path

import structlog

log = structlog.get_logger("static_server")

mimetypes.add_type("text/vtt", ".vtt")
mimetypes.add_type("video/iso.segment", ".m4s")

# Los segmentos y el init nunca cambian una vez escritos; las playlists y los
# subtitulos si.
IMMUTABLE_CACHE = "public, max-age=31536000, immutable"
NO_CACHE = "no-store"
IMMUTABLE_SUFFIXES = (".m4s", ".mp4", ".ts")

# El navegador aborta descargas todo el tiempo (seek, cambio de pista, cerrar
# pestana). Eso corta el socket a mitad de un segmento y no es un error.
CLIENT_DISCONNECTS = (BrokenPipeError, ConnectionResetError, ConnectionAbortedError)


def cache_control_for(path: str) -> str:
    """Politica de cache segun el tipo de archivo pedido."""
    return IMMUTABLE_CACHE if path.endswith(IMMUTABLE_SUFFIXES) else NO_CACHE


class QuietHandler(http.server.SimpleHTTPRequestHandler):
    """Handler estatico sin log por request, con CORS y cache por tipo."""

    # HTTP/1.1 habilita keep-alive: una conexion por segmento es carisima
    # cuando el video tiene miles de segmentos.
    protocol_version = "HTTP/1.1"

    # Prefijo de URL -> directorio en disco, para lo que no cuelga de la raiz.
    # Lo llena `start_server`.
    extra: dict[str, Path] = {}

    def translate_path(self, path: str) -> str:
        """Resuelve contra el primer prefijo que matchee, o contra la raiz.

        `SimpleHTTPRequestHandler` sirve un solo directorio y el modo CLI
        necesita dos que no viven juntos en disco: el frontend y la salida.

        El resto de la URL lo escribe quien pide, asi que se resuelve y se
        comprueba que siga adentro del directorio: sin eso un `..` se escapa.
        """
        clean = urllib.parse.unquote(urllib.parse.urlsplit(path).path)
        for prefix, directory in self.extra.items():
            if clean == prefix or clean.startswith(prefix + "/"):
                rest = clean[len(prefix):].lstrip("/")
                root = directory.resolve()
                target = (root / rest).resolve()
                # Fuera del directorio: devolver la raiz y que conteste 404.
                return str(target if target.is_relative_to(root) else root)
        return super().translate_path(path)

    def end_headers(self) -> None:
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Cache-Control", cache_control_for(self.path))
        super().end_headers()

    def copyfile(self, source, outputfile) -> None:
        """Ignora el corte del cliente a mitad de una descarga."""
        try:
            super().copyfile(source, outputfile)
        except CLIENT_DISCONNECTS:
            # La conexion quedo a medias: no se puede reusar.
            self.close_connection = True

    def log_message(self, format: str, *args) -> None:
        pass


class QuietThreadingHTTPServer(socketserver.ThreadingTCPServer):
    """Servidor que no ensucia el log con desconexiones del navegador."""

    daemon_threads = True
    allow_reuse_address = True

    def handle_error(self, request, client_address) -> None:
        if isinstance(sys.exc_info()[1], CLIENT_DISCONNECTS):
            return
        super().handle_error(request, client_address)


def start_server(
    root: Path, port: int, extra: dict[str, Path] | None = None
) -> socketserver.TCPServer:
    """Levanta el servidor en un thread daemon y lo devuelve ya corriendo.

    `extra` mapea prefijos de URL a directorios fuera de `root`.
    """
    log.info("iniciando servidor estatico", root=str(root), port=port)
    handler = functools.partial(QuietHandler, directory=str(root))
    # De clase y no de instancia: el handler se construye por request.
    QuietHandler.extra = extra or {}
    server = QuietThreadingHTTPServer(("0.0.0.0", port), handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server


def mounted_url(prefix: str, root: Path, target: Path) -> str:
    """URL de un archivo servido bajo el prefijo con el que se monto `root`.

    Reemplaza al calculo contra el directorio del proyecto: ahora la salida no
    tiene que vivir adentro de la raiz, alcanza con que este montada.
    """
    return prefix.rstrip("/") + "/" + target.resolve().relative_to(root.resolve()).as_posix()
