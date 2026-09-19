"""Consultas sobre la tabla `media`.

Esta capa sabe de SQL y de nada mas: no valida rutas, no llama a ffprobe y no
decide codigos HTTP. Recibe la conexion ya abierta — quien la crea, le carga el
esquema y la cierra es `manager.entityManager` — asi que tampoco administra el
ciclo de vida de la base.
"""

from __future__ import annotations

import json
from datetime import datetime, timezone
from pathlib import Path
from sqlite3 import Connection, Row

from app.models.media import Media, path_key_for

# Columnas que el PATCH puede tocar. Es tambien la lista blanca que evita que
# un nombre de campo llegue crudo al SQL.
EDITABLE = frozenset(
    {"title", "kind", "year", "synopsis", "genres", "notes", "in_list"}
)

# Columnas que se guardan como JSON.
_JSON_COLUMNS = ("info", "genres")

# El ORDER BY nunca se interpola: se elige de aca.
#
# El desempate por `rowid` no es decorativo: `created_at` tiene resolucion de
# segundos, asi que dos altas seguidas empatan y "Recently added" las devuelve
# en orden arbitrario. Con un alta manual por vez casi no se nota; con una carga
# masiva de un directorio, toda la tanda queda desordenada. `rowid` es
# monotonico por insercion y no depende del reloj.
_SORTS = {
    "title": "title COLLATE NOCASE ASC",
    "added": "created_at DESC, rowid DESC",
}


def now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def _to_row(media: Media) -> dict:
    row = vars(media).copy()
    row["in_list"] = int(media.in_list)
    for column in _JSON_COLUMNS:
        row[column] = json.dumps(row[column])
    return row


def _to_media(row: Row) -> Media:
    data = dict(row)
    data["in_list"] = bool(data["in_list"])
    for column in _JSON_COLUMNS:
        data[column] = json.loads(data[column]) if data[column] else None
    data["genres"] = data["genres"] or []
    data["info"] = data["info"] or {}
    return Media(**data)


def _filters(
    q: str | None, kind: str | None, in_list: bool | None
) -> tuple[str, tuple]:
    """Arma el WHERE compartido por `list` y `count`."""
    clauses, params = [], []
    if q:
        clauses.append("title LIKE ?")
        params.append(f"%{q}%")
    if kind:
        clauses.append("kind = ?")
        params.append(kind)
    if in_list is not None:
        clauses.append("in_list = ?")
        params.append(int(in_list))
    return ("WHERE " + " AND ".join(clauses)) if clauses else "", tuple(params)


class MediaRepository:
    """Acceso a la tabla `media`.

    Los metodos son sincronicos a proposito: sqlite3 es stdlib y una consulta
    sobre un catalogo casero tarda microsegundos, asi que no justifica una
    dependencia nueva ni un pool.

    ponytail: una conexion compartida y llamadas bloqueantes. Si alguna vez el
    catalogo crece hasta que una query se note en el event loop, envolver los
    metodos en `asyncio.to_thread` o pasar a aiosqlite.
    """

    def __init__(self, db: Connection) -> None:
        self._db = db

    # --- Escritura ----------------------------------------------------------

    def add(self, media: Media) -> Media:
        """Inserta la ficha. Levanta `sqlite3.IntegrityError` si ya existe."""
        media.created_at = media.created_at or now()
        media.updated_at = now()
        row = _to_row(media)
        columns = ", ".join(row)
        placeholders = ", ".join(f":{c}" for c in row)
        with self._db:
            self._db.execute(
                f"INSERT INTO media ({columns}) VALUES ({placeholders})", row
            )
        return media

    def update(self, id_media: str, **fields) -> Media | None:
        """Actualiza campos editoriales. Ignora los que no lo son.

        Lo que llega, se escribe — `None` incluido, que es como se vacia un
        campo. Distinguir "no lo mandaron" de "lo mandaron en null" es cosa del
        handler, que arma estos `fields` con `model_dump(exclude_unset=True)`:
        aca abajo ya no queda ambiguedad que resolver.
        """
        changes = {k: v for k, v in fields.items() if k in EDITABLE}
        if changes:
            if changes.get("genres") is not None:
                changes["genres"] = json.dumps(changes["genres"])
            if changes.get("in_list") is not None:
                changes["in_list"] = int(changes["in_list"])
            changes["updated_at"] = now()
            assignments = ", ".join(f"{c} = :{c}" for c in changes)
            with self._db:
                self._db.execute(
                    f"UPDATE media SET {assignments} WHERE id_media = :id_media",
                    {**changes, "id_media": id_media},
                )
        return self.get(id_media)

    def set_info(self, id_media: str, info: dict) -> None:
        """Reescribe el blob derivado completo.

        Queda fuera de `EDITABLE` a proposito, igual que `set_asset`: no es una
        puerta para editar `info` desde la API —`MediaPatch` no acepta ese
        campo— sino la unica forma de tocar los `ignore` de las pistas, que son
        lo unico de ese blob que decide el usuario. Quien llama arma el dict
        con `media_analyzer.to_dict`, asi que la forma la sigue dictando
        `SourceInfo`.
        """
        with self._db:
            self._db.execute(
                "UPDATE media SET info = ?, updated_at = ? WHERE id_media = ?",
                (json.dumps(info), now(), id_media),
            )

    def set_asset(self, id_media: str, asset_id: str) -> None:
        """Enlaza la ficha con su cache HLS. Cambia cuando cambia el archivo."""
        with self._db:
            self._db.execute(
                "UPDATE media SET asset_id = ?, updated_at = ? WHERE id_media = ?",
                (asset_id, now(), id_media),
            )

    def delete(self, id_media: str) -> bool:
        with self._db:
            cursor = self._db.execute(
                "DELETE FROM media WHERE id_media = ?", (id_media,)
            )
        return cursor.rowcount > 0

    # --- Lectura ------------------------------------------------------------

    def get(self, id_media: str) -> Media | None:
        row = self._db.execute(
            "SELECT * FROM media WHERE id_media = ?", (id_media,)
        ).fetchone()
        return _to_media(row) if row else None

    def by_path(self, file_path: str | Path) -> Media | None:
        """Busca por ruta normalizada. Es el chequeo de duplicados del alta."""
        row = self._db.execute(
            "SELECT * FROM media WHERE path_key = ?", (path_key_for(file_path),)
        ).fetchone()
        return _to_media(row) if row else None

    def list(
        self,
        *,
        q: str | None = None,
        kind: str | None = None,
        in_list: bool | None = None,
        sort: str = "title",
        limit: int = 10,
        offset: int = 0,
    ) -> list[Media]:
        where, params = _filters(q, kind, in_list)
        order = _SORTS.get(sort, _SORTS["title"])
        rows = self._db.execute(
            f"SELECT * FROM media {where} ORDER BY {order} LIMIT ? OFFSET ?",
            (*params, limit, offset),
        ).fetchall()
        return [_to_media(row) for row in rows]

    def count(
        self,
        *,
        q: str | None = None,
        kind: str | None = None,
        in_list: bool | None = None,
    ) -> int:
        """Total con los mismos filtros que `list`: es el "6 titles" de la UI."""
        where, params = _filters(q, kind, in_list)
        return self._db.execute(
            f"SELECT COUNT(*) FROM media {where}", params
        ).fetchone()[0]
