"""Excepciones compartidas por los servicios."""

from __future__ import annotations


class FFmpegError(RuntimeError):
    """FFmpeg o ffprobe terminaron con codigo distinto de cero.

    Guarda el stderr completo para poder mostrarlo al depurar.
    """

    def __init__(self, command: str, returncode: int, stderr: str) -> None:
        self.command = command
        self.returncode = returncode
        self.stderr = stderr
        super().__init__(f"{command} fallo con codigo {returncode}:\n{stderr}")
