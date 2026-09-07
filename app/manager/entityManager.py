"""Ciclo de vida de la base de datos del ABM.

Es la unica capa que abre, configura y cierra la conexion. Los repositorios la
reciben ya lista y solo consultan: asi no hay una clase que administre la base
*y* ademas escriba SQL.

El DDL vive en `schema.sql`, no en un string de Python: se lee con resaltado de
sintaxis, se puede correr a mano contra el `.sqlite` y el diff de una columna
nueva se ve como SQL.
"""

from __future__ import annotations

import sqlite3
from pathlib import Path
from sqlite3 import Connection

SCHEMA_FILE = Path(__file__).parent / "schema.sql"

MEMORY = ":memory:"


def connect(db_path: Path | str) -> Connection:
    """Abre la BD del ABM y devuelve la conexion con el esquema ya cargado.

    Es lo que se le pasa a un repositorio. `check_same_thread=False` porque
    FastAPI atiende los handlers sincronicos en un threadpool, no siempre en el
    mismo hilo.
    """
    if str(db_path) != MEMORY:
        Path(db_path).parent.mkdir(parents=True, exist_ok=True)

    db = sqlite3.connect(str(db_path), check_same_thread=False)
    db.row_factory = sqlite3.Row
    # WAL: sin esto un alta que esta escribiendo bloquea los listados.
    db.execute("PRAGMA journal_mode=WAL")
    load_db_schema(db)
    return db


def load_db_schema(db: Connection) -> None:
    """Carga el esquema de la base de datos desde el archivo `schema.sql`.

    Todo el DDL es `IF NOT EXISTS`, asi que correrlo en cada arranque es
    inofensivo y hace las veces de creacion inicial.
    """
    db.executescript(SCHEMA_FILE.read_text(encoding="utf-8"))
