"""Modelos Pydantic v2 para request/response de la API."""

from __future__ import annotations

from typing import Literal

from pydantic import BaseModel, Field, model_validator

MediaKind = Literal["film", "series", "documentary"]


class StreamRequest(BaseModel):
    """Que reproducir: una ficha del catalogo, o una ruta absoluta.

    `id_media` es el camino de la galeria, que no conoce `MEDIA_ROOT` y no tiene
    por que: armar la absoluta en el cliente seria hardcodear el punto de montaje
    del server. `file_path` es el del ingreso manual y el CLI, y es el que F1 del
    plan de hardening va a sacar.

    Exactamente uno de los dos. Aceptar los dos obligaria a elegir cual gana
    cuando no coinciden, y esa es una ambiguedad que le toca resolver a quien
    llama, no al servidor.
    """

    file_path: str | None = None
    id_media: str | None = None

    @model_validator(mode="after")
    def exactly_one_source(self) -> StreamRequest:
        # La comparacion cubre los dos casos de una: ninguno y los dos.
        if (self.file_path is None) == (self.id_media is None):
            raise ValueError("Mandar exactamente uno de id_media o file_path")
        return self


class AudioTrackSchema(BaseModel):
    index: int
    codec: str
    channels: int
    language: str
    title: str
    # Lo unico de la pista que decide el usuario: True = no se genera. Tiene
    # default para que la respuesta del player, que todavia no lo usa, no
    # tenga que pasarlo.
    ignore: bool = False


class SubtitleTrackSchema(BaseModel):
    index: int
    codec: str
    language: str
    title: str
    ignore: bool = False
    url: str | None = None


class StreamResponse(BaseModel):
    """Todo lo que el player necesita para arrancar.

    No hay endpoint de seleccion de pista ni de seek: el master declara las
    pistas de audio como renditions y el timeline es absoluto, asi que las dos
    cosas las resuelve el cliente sin volver al servidor.
    """

    session_id: str
    asset_id: str
    status: str          # processing | ready | failed
    playable: bool       # ya hay segmentos suficientes para empezar
    master_url: str
    duration_seconds: float
    progress: float      # 0.0 a 1.0, avance del build de video
    strategy: str
    audio_tracks: list[AudioTrackSchema]
    subtitle_tracks: list[SubtitleTrackSchema]
    error: str | None = None


class ErrorResponse(BaseModel):
    error: str
    detail: str


class MediaCreate(BaseModel):
    """Alta de una ficha.

    La ruta va **relativa a `MEDIA_ROOT`** ("films/Dune.mkv"): el formulario no
    deberia tener que tipear el punto de montaje. Es al reves que
    `StreamRequest`, que la recibe absoluta, y por eso son dos modelos.
    """

    file_path: str


class MediaResponse(BaseModel):
    """Una ficha completa.

    Los datos derivados del archivo salen aplanados y no adentro de `info`: el
    frontend no tiene por que conocer la forma interna de `SourceInfo`.
    """

    id_media: str
    file_path: str          # relativa a MEDIA_ROOT
    file_name: str
    asset_id: str | None    # enlace con el cache HLS
    # Editoriales
    title: str
    kind: str
    year: int | None
    synopsis: str | None
    genres: list[str]
    notes: str | None
    in_list: bool
    # Derivados del archivo
    duration: float
    video_codec: str
    width: int | None
    height: int | None
    strategy: str
    audio_tracks: list[AudioTrackSchema]
    subtitle_tracks: list[SubtitleTrackSchema]
    # Auditoria
    created_at: str
    updated_at: str


class MediaListItem(BaseModel):
    """Una fila de la grilla de la galeria.

    Lo minimo para dibujar una tarjeta: el titulo y la linea de abajo, que
    segun la seccion es "Film · 1 h 52" o solo "1 h 04". Todo lo demas que
    muestra la galeria — sinopsis, pistas, resolucion — vive en el panel de
    detalle, o sea en `GET /media/{id_media}`, y mandarlo en cada fila serian
    treinta arrays de pistas por pagina para dibujar cero pixeles.
    """

    id_media: str
    title: str
    kind: str
    year: int | None
    duration: float


class MediaListResponse(BaseModel):
    """Una pagina del catalogo.

    `total` es el contador del encabezado de cada seccion ("6 titles") y no
    cambia con la paginacion: es lo que permite dibujar "pagina 1 de N".
    """

    items: list[MediaListItem]
    total: int
    limit: int
    offset: int


class MediaPatch(BaseModel):
    """Campos editoriales de una ficha. Todos opcionales.

    Los campos derivados del archivo (codecs, duracion, pistas) no estan aca a
    proposito: los dicta ffprobe y editarlos a mano haria mentir a la ficha.
    `extra: forbid` hace que mandarlos sea un 422 y no un cambio silencioso.

    Ausente y nulo significan cosas distintas: **ausente** es "no lo toques" y
    **nulo** es "vacialo". El handler los separa con `model_dump(exclude_unset=True)`.

    Por eso los tres campos que en la tabla son `NOT NULL` se declaran sin
    `| None`: mandarlos en null es un 422 y no un 500 de la base. Los otros
    cuatro si se pueden vaciar.
    """

    model_config = {"extra": "forbid"}

    # NOT NULL en la tabla: se pueden cambiar, no vaciar.
    title: str = Field(default=None)
    kind: MediaKind = Field(default=None)
    in_list: bool = Field(default=None)
    # Las pistas que NO se generan. No son columnas: viajan al flag `ignore` de
    # cada pista adentro de `info`, y por eso tampoco estan en `EDITABLE`.
    #
    # La lista reemplaza al estado anterior, asi que `[]` significa "ninguna
    # ignorada" y mandarla en null es un 422: vaciar no quiere decir nada acá,
    # el conjunto vacio ya se escribe `[]`.
    ignored_audio: list[int] = Field(default=None)
    ignored_subtitles: list[int] = Field(default=None)
    # Nullables: mandarlos en null los borra.
    year: int | None = None
    synopsis: str | None = None
    genres: list[str] | None = None
    notes: str | None = None


class ProgressUpdate(BaseModel):
    """Posicion del espectador dentro del archivo, en segundos."""

    position: float
