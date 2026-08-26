"""Mocks compartidos: ningun test invoca los binarios reales."""

from __future__ import annotations

import io
import json

import pytest
import structlog

from app.services.media_analyzer import AudioTrack, SourceInfo, SubtitleTrack


class _NullLoggerFactory:
    """Descarta todos los logs en tests para no depender de stderr."""

    def __call__(self, *args, **kwargs):
        return structlog.PrintLogger(file=io.StringIO())


structlog.configure(
    processors=[
        structlog.processors.add_log_level,
        structlog.processors.JSONRenderer(),
    ],
    wrapper_class=structlog.make_filtering_bound_logger(0),
    context_class=dict,
    logger_factory=_NullLoggerFactory(),
    cache_logger_on_first_use=False,
)


class FakeStreamReader:
    """StreamReader falso que entrega lineas ya preparadas y despues EOF."""

    def __init__(self, lines: list[bytes] | None = None) -> None:
        self._lines = list(lines or [])

    async def readline(self) -> bytes:
        if not self._lines:
            return b""
        return self._lines.pop(0)


class FakeProcess:
    """Sustituto de `asyncio.subprocess.Process`.

    Cubre las dos formas de uso del proyecto: `communicate()` (ffprobe) y
    lectura incremental de stdout/stderr (ffmpeg con -progress).
    """

    def __init__(
        self,
        returncode: int = 0,
        stdout: bytes = b"",
        stderr: bytes = b"",
        stdout_lines: list[bytes] | None = None,
        stderr_lines: list[bytes] | None = None,
    ) -> None:
        self.returncode = returncode
        self._stdout = stdout
        self._stderr = stderr
        self.stdout = FakeStreamReader(stdout_lines)
        self.stderr = FakeStreamReader(stderr_lines)
        self.killed = False

    async def communicate(self) -> tuple[bytes, bytes]:
        return self._stdout, self._stderr

    async def wait(self) -> int:
        return self.returncode

    def kill(self) -> None:
        self.killed = True


@pytest.fixture
def spawn_mock(monkeypatch):
    """Parchea `create_subprocess_exec` en un modulo y registra la llamada.

    Devuelve una funcion `spawn_mock(modulo, proceso)` que retorna una lista
    donde queda guardado el comando con el que se invoco el subproceso.
    """

    def install(module, process: FakeProcess) -> list[tuple[str, ...]]:
        calls: list[tuple[str, ...]] = []

        async def fake_exec(*args, **kwargs):
            calls.append(args)
            return process

        monkeypatch.setattr(module.asyncio, "create_subprocess_exec", fake_exec)
        return calls

    return install


def probe_payload(
    *,
    video_codec: str = "h264",
    audio_codecs: tuple[str, ...] = ("aac",),
    channels: int = 2,
    duration: str = "120.5",
    subtitle_codecs: tuple[str, ...] = ("subrip",),
) -> bytes:
    """Salida JSON de ffprobe lista para usar como stdout del mock."""
    _sub_langs = ("spa", "eng", "por", "fra")
    streams: list[dict] = [
        {
            "codec_type": "video",
            "codec_name": video_codec,
            "width": 1920,
            "height": 1080,
        }
    ]
    for i, codec in enumerate(audio_codecs):
        streams.append(
            {
                "codec_type": "audio",
                "codec_name": codec,
                "channels": channels,
                "tags": {"language": ["spa", "eng"][i % 2]},
            }
        )
    for i, codec in enumerate(subtitle_codecs):
        streams.append(
            {
                "codec_type": "subtitle",
                "codec_name": codec,
                "tags": {"language": _sub_langs[i % len(_sub_langs)]},
            }
        )

    return json.dumps({"streams": streams, "format": {"duration": duration}}).encode()


@pytest.fixture
def h264_aac() -> SourceInfo:
    """Origen que no necesita re-codificacion."""
    return SourceInfo(
        video_codec="h264", width=1280, height=720, duration=100.0,
        audio_codec="aac", audio_channels=2, audio_language="spa", audio_count=1,
        audio_tracks=(AudioTrack(index=0, codec="aac", channels=2, language="spa", title=""),),
        subtitle_tracks=(),
    )


@pytest.fixture
def hevc_ac3() -> SourceInfo:
    """Origen que necesita transcodificar video y audio."""
    return SourceInfo(
        video_codec="hevc", width=3840, height=2160, duration=7200.0,
        audio_codec="ac3", audio_channels=6, audio_language="eng", audio_count=2,
        audio_tracks=(
            AudioTrack(index=0, codec="ac3", channels=6, language="eng", title=""),
            AudioTrack(index=1, codec="aac", channels=2, language="spa", title=""),
        ),
        subtitle_tracks=(
            SubtitleTrack(index=0, codec="subrip", language="eng", title=""),
        ),
    )
