"""Modelos Pydantic v2 para request/response de la API."""

from __future__ import annotations

from pydantic import BaseModel


class StreamRequest(BaseModel):
    file_path: str


class AudioTrackSchema(BaseModel):
    index: int
    codec: str
    channels: int
    language: str
    title: str


class SubtitleTrackSchema(BaseModel):
    index: int
    codec: str
    language: str
    title: str
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
