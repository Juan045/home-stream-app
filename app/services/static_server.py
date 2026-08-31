"""Servidor de archivos estaticos para el MVP.

Sirve el player y el directorio de salida mientras FFmpeg genera los segmentos.
Provisorio: lo reemplaza FastAPI cuando exista la API.
"""

from __future__ import annotations

import functools
import http.server
import mimetypes
import socketserver
import sys
import threading
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


def start_server(root: Path, port: int) -> socketserver.TCPServer:
    """Levanta el servidor en un thread daemon y lo devuelve ya corriendo."""
    log.info("iniciando servidor estatico", root=str(root), port=port)
    handler = functools.partial(QuietHandler, directory=str(root))
    server = QuietThreadingHTTPServer(("0.0.0.0", port), handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server


def manifest_url(root: Path, output_dir: Path, manifest: str = "master.m3u8") -> str | None:
    """URL relativa del manifest, o None si la salida cae fuera de `root`."""
    if not output_dir.is_relative_to(root):
        return None
    return "/" + (output_dir.relative_to(root) / manifest).as_posix()


def player_url(port: int, manifest: str) -> str:
    """URL completa del player apuntando al manifest indicado."""
    return f"http://localhost:{port}/static/player.html?src={manifest}"
