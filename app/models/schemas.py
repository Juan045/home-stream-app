"""Modelos Pydantic v2 para request/response de la API."""

from __future__ import annotations

from pydantic import BaseModel


class StreamRequest(BaseModel):
    file_path: str


class SelectRequest(BaseModel):
    audio_track: int | None = None
    subtitle_track: int | None = None
    timestamp: float = 0


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


class JobResponse(BaseModel):
    job_id: str
    status: str
    strategy: str
    hls_url: str
    duration_seconds: float
    current_audio_track: int
    audio_tracks: list[AudioTrackSchema]
    subtitle_tracks: list[SubtitleTrackSchema]
    error: str | None = None


class SelectResponse(BaseModel):
    job_id: str
    status: str
    current_audio_track: int
    hls_url: str
