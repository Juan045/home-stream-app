-- Esquema del catalogo del ABM.
--
-- Los datos derivados del archivo (codecs, pistas, resolucion) viven
-- serializados en `info` asi la tabla no migra cada vez que SourceInfo gana un campo. 
-- Son columnas propias solo los campos por los que el listado filtra u ordena.

CREATE TABLE IF NOT EXISTS media (
    id_media   TEXT PRIMARY KEY,
    file_path  TEXT NOT NULL,
    path_key   TEXT NOT NULL UNIQUE,
    file_name  TEXT NOT NULL,
    asset_id   TEXT,
    duration   REAL NOT NULL DEFAULT 0,
    info       TEXT NOT NULL DEFAULT '{}',
    title      TEXT NOT NULL,
    kind       TEXT NOT NULL DEFAULT 'film',
    year       INTEGER,
    synopsis   TEXT,
    genres     TEXT,
    notes      TEXT,
    in_list    INTEGER NOT NULL DEFAULT 0,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_media_title ON media(title COLLATE NOCASE);