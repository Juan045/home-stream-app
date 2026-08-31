"""Tests de session_manager: TTL, heartbeat y expiracion con reloj falso."""

from __future__ import annotations

import pytest

from app.services.session_manager import SessionManager


class FakeClock:
    """Reloj controlable: los tests no esperan tiempo real."""

    def __init__(self) -> None:
        self.now = 1000.0

    def __call__(self) -> float:
        return self.now

    def advance(self, seconds: float) -> None:
        self.now += seconds


@pytest.fixture
def clock() -> FakeClock:
    return FakeClock()


@pytest.fixture
def manager(clock: FakeClock) -> SessionManager:
    return SessionManager(inactive_after=120.0, expire_after=720.0, clock=clock)


def test_create_devuelve_una_sesion_viva(manager):
    session = manager.create("asset-1")

    assert manager.get(session.id) is session
    assert manager.is_active(session.id)
    assert len(manager) == 1


def test_cada_sesion_tiene_id_propio(manager):
    assert manager.create("asset-1").id != manager.create("asset-1").id


def test_get_de_sesion_inexistente(manager):
    assert manager.get("no-existe") is None
    assert not manager.is_active("no-existe")


# --- Heartbeat e inactividad ------------------------------------------------

def test_sin_heartbeat_la_sesion_se_vuelve_inactiva(manager, clock):
    session = manager.create("asset-1")

    clock.advance(121.0)

    assert not manager.is_active(session.id)
    assert manager.get(session.id) is not None  # todavia no expiro


def test_el_heartbeat_la_mantiene_activa(manager, clock):
    session = manager.create("asset-1")

    for _ in range(10):
        clock.advance(30.0)  # el player late cada 30 s
        assert manager.heartbeat(session.id)

    assert manager.is_active(session.id)


def test_heartbeat_de_sesion_inexistente(manager):
    assert manager.heartbeat("no-existe") is False


# --- Expiracion -------------------------------------------------------------

def test_prune_elimina_las_expiradas(manager, clock):
    viva = manager.create("asset-1")
    muerta = manager.create("asset-2")

    clock.advance(721.0)
    manager.heartbeat(viva.id)

    assert manager.prune() == [muerta.id]
    assert manager.get(viva.id) is not None
    assert manager.get(muerta.id) is None


def test_prune_no_toca_las_inactivas_recientes(manager, clock):
    # Inactiva no es lo mismo que expirada: se conserva por si el usuario vuelve.
    session = manager.create("asset-1")
    clock.advance(200.0)

    assert manager.prune() == []
    assert not manager.is_active(session.id)
    assert manager.get(session.id) is not None


def test_prune_sin_sesiones(manager):
    assert manager.prune() == []


def test_close_elimina_la_sesion(manager):
    session = manager.create("asset-1")

    manager.close(session.id)

    assert manager.get(session.id) is None
    assert len(manager) == 0


def test_close_de_sesion_inexistente_no_falla(manager):
    manager.close("no-existe")


# --- Proteccion de assets ---------------------------------------------------

def test_active_asset_ids_protege_lo_que_se_esta_mirando(manager):
    manager.create("asset-1")
    manager.create("asset-2")
    manager.create("asset-1")

    assert manager.active_asset_ids() == {"asset-1", "asset-2"}


def test_un_asset_sin_espectadores_activos_deja_de_estar_protegido(manager, clock):
    manager.create("asset-1")

    clock.advance(121.0)

    # Ya nadie lo esta mirando: el GC puede barrerlo si necesita espacio.
    assert manager.active_asset_ids() == set()
    assert manager.referenced_asset_ids() == {"asset-1"}


def test_referenced_ignora_las_sesiones_podadas(manager, clock):
    manager.create("asset-1")

    clock.advance(721.0)
    manager.prune()

    assert manager.referenced_asset_ids() == set()
