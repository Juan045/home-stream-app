"""Sesiones de reproduccion: heartbeat, TTL y expiracion.

Una sesion es un espectador, no un trabajo. No es duena de ningun archivo: solo
apunta a un asset y lo mantiene protegido del GC mientras alguien lo este
mirando. Cuando la ultima sesion de un asset expira, el asset queda disponible
para que el LRU lo barra si hace falta espacio.
"""

from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Callable
from uuid import uuid4

import structlog

log = structlog.get_logger("session_manager")

# El player manda heartbeat cada 30 s. Dos minutos sin noticias es una pestana
# cerrada o una conexion caida.
INACTIVE_AFTER = 120.0

# Se conserva un rato mas por si el usuario vuelve: recargar la pagina no
# deberia costar reconstruir nada.
EXPIRE_AFTER = 720.0


@dataclass
class Session:
    """Un espectador mirando un asset."""

    id: str
    asset_id: str
    created_at: float
    last_seen: float


class SessionManager:
    """Estado en memoria de las sesiones activas."""

    def __init__(
        self,
        inactive_after: float = INACTIVE_AFTER,
        expire_after: float = EXPIRE_AFTER,
        clock: Callable[[], float] = time.monotonic,
    ) -> None:
        self._sessions: dict[str, Session] = {}
        self._inactive_after = inactive_after
        self._expire_after = expire_after
        self._clock = clock

    def create(self, asset_id: str) -> Session:
        now = self._clock()
        session = Session(
            id=uuid4().hex[:12], asset_id=asset_id, created_at=now, last_seen=now,
        )
        self._sessions[session.id] = session
        log.info("sesion creada", session_id=session.id, asset_id=asset_id)
        return session

    def get(self, session_id: str) -> Session | None:
        return self._sessions.get(session_id)

    def heartbeat(self, session_id: str) -> bool:
        """Marca la sesion como viva. False si no existe (o ya expiro)."""
        session = self._sessions.get(session_id)
        if session is None:
            return False
        session.last_seen = self._clock()
        return True

    def close(self, session_id: str) -> None:
        self._sessions.pop(session_id, None)

    def is_active(self, session_id: str) -> bool:
        session = self._sessions.get(session_id)
        if session is None:
            return False
        return self._clock() - session.last_seen < self._inactive_after

    def active_asset_ids(self) -> set[str]:
        """Assets que alguien esta mirando. El GC no puede tocarlos."""
        return {
            s.asset_id
            for s in self._sessions.values()
            if self._clock() - s.last_seen < self._inactive_after
        }

    def referenced_asset_ids(self) -> set[str]:
        """Assets con alguna sesion viva, activa o no."""
        return {s.asset_id for s in self._sessions.values()}

    def prune(self) -> list[str]:
        """Elimina las sesiones expiradas. Devuelve sus ids."""
        now = self._clock()
        expired = [
            session_id
            for session_id, session in self._sessions.items()
            if now - session.last_seen >= self._expire_after
        ]
        for session_id in expired:
            del self._sessions[session_id]

        if expired:
            log.info("sesiones expiradas", count=len(expired))
        return expired

    def __len__(self) -> int:
        return len(self._sessions)
