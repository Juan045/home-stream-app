"""Excepciones compartidas por los servicios."""

from __future__ import annotations


class ApiError(Exception):
    """Error de la API con el formato de respuesta documentado.

    Se serializa como `{"error": "<slug>", "detail": "<texto>"}`, que es lo que
    el player espera para mostrar algo util en vez de un stack trace.
    """

    def __init__(self, status_code: int, error: str, detail: str) -> None:
        self.status_code = status_code
        self.error = error
        self.detail = detail
        super().__init__(f"{error}: {detail}")


class FFmpegError(RuntimeError):
    """FFmpeg o ffprobe terminaron con codigo distinto de cero.

    Guarda el stderr completo para poder mostrarlo al depurar.
    """

    def __init__(self, command: str, returncode: int, stderr: str) -> None:
        self.command = command
        self.returncode = returncode
        self.stderr = stderr
        super().__init__(f"{command} fallo con codigo {returncode}:\n{stderr}")
