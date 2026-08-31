"""Construccion y estado de los artefactos de un asset.

Reemplaza al viejo `job_manager`. La diferencia de fondo: aca no hay "un stream
generandose para un espectador" que haya que matar y reiniciar cada vez que
alguien cambia de idioma. Hay un archivo y sus artefactos derivados —video,
una pista de audio por idioma, subtitulos— que se construyen una sola vez y
quedan cacheados.

El video se genera sin audio y cada pista de audio por separado, todas con el
mismo origen de tiempo (`-copyts`). Eso es lo que permite declararlas como
renditions en el master: cambiar de idioma pasa a ser cosa del cliente y no
vuelve a tocar FFmpeg.
"""

from __future__ import annotations

import asyncio
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path
from typing import Callable

import structlog

from app.errors import FFmpegError
from app.services import media_analyzer, playlist
from app.services.asset_store import AssetPaths, AssetStore, asset_id_for
from app.services.media_analyzer import AudioTrack, SourceInfo, analyze
from app.services.transcoder import (
    ManagedFFmpeg,
    TranscodeOptions,
    audio_copy_allowed,
    build_audio_args,
    build_video_args,
    extract_subtitle,
    start_ffmpeg,
    video_copy_allowed,
)

log = structlog.get_logger("asset_builder")

VIDEO_KEY = "video"

# Segmentos que tienen que existir antes de dejar que el player arranque. Con
# uno solo alcanza para reproducir, pero dos le dan margen al reload de la
# playlist EVENT y evitan un stall inmediato.
PLAYABLE_SEGMENTS = 2
PLAYABLE_POLL_INTERVAL = 0.2
PLAYABLE_TIMEOUT = 20.0

# Si ffprobe no reporta bitrate, hay que declarar algo en el master.
FALLBACK_BANDWIDTH = 4_000_000

# El video re-codificado sale siempre con estos parametros fijos (ver
# `_libx264_args`), asi que su string de codec es conocido de antemano.
TRANSCODED_VIDEO_CODEC = "avc1.640029"


class ArtifactState(str, Enum):
    PENDING = "pending"
    BUILDING = "building"
    READY = "ready"
    FAILED = "failed"


@dataclass
class Artifact:
    """Estado de una pista generada (el video, o una pista de audio)."""

    state: ArtifactState = ArtifactState.PENDING
    error: str | None = None
    seconds_done: float = 0.0


@dataclass
class Asset:
    """Un archivo de origen y todo lo que se genero a partir de el."""

    id: str
    source: Path
    paths: AssetPaths
    info: SourceInfo
    options: TranscodeOptions
    video: Artifact = field(default_factory=Artifact)
    audio: dict[int, Artifact] = field(default_factory=dict)
    subtitles: dict[int, str] = field(default_factory=dict)

    @property
    def status(self) -> str:
        if self.video.state is ArtifactState.FAILED:
            return "failed"
        states = [self.video.state, *(a.state for a in self.audio.values())]
        if all(s is ArtifactState.READY for s in states):
            return "ready"
        return "processing"

    @property
    def error(self) -> str | None:
        if self.video.error:
            return self.video.error
        return next((a.error for a in self.audio.values() if a.error), None)

    @property
    def progress(self) -> float:
        """Avance del build de video, entre 0 y 1."""
        if self.info.duration <= 0:
            return 0.0
        if self.video.state is ArtifactState.READY:
            return 1.0
        return min(self.video.seconds_done / self.info.duration, 1.0)


class AssetBuilder:
    """Abre assets, lanza los builds que falten y arma las playlists."""

    def __init__(
        self,
        store: AssetStore,
        options: TranscodeOptions,
        max_concurrent: int = 3,
        audio_tracks: set[int] | None = None,
        on_progress: Callable[[str, float], None] | None = None,
    ) -> None:
        self._store = store
        self._options = options
        self._audio_tracks = audio_tracks
        self.on_progress = on_progress
        self._assets: dict[str, Asset] = {}
        self._locks: dict[str, asyncio.Lock] = {}
        self._semaphore = asyncio.Semaphore(max_concurrent)
        self._tasks: set[asyncio.Task] = set()
        self._processes: dict[tuple[str, str], ManagedFFmpeg] = {}

    # --- Ciclo de vida ------------------------------------------------------

    async def open(self, source: Path) -> Asset:
        """Devuelve el asset del archivo, construyendo lo que falte.

        Es idempotente: abrir dos veces la misma pelicula no lanza dos FFmpeg,
        y si el cache ya la tiene completa no lanza ninguno.
        """
        asset_id = asset_id_for(source)
        lock = self._locks.setdefault(asset_id, asyncio.Lock())

        async with lock:
            asset = self._assets.get(asset_id)
            if asset is None:
                asset = await self._load(asset_id, source)
                self._assets[asset_id] = asset
            self._store.touch(asset_id)
            self._start_pending_builds(asset)

        await self._wait_until_playable(asset)
        return asset

    def get(self, asset_id: str) -> Asset | None:
        return self._assets.get(asset_id)

    def active_ids(self) -> set[str]:
        return set(self._assets)

    def building_count(self) -> int:
        """Artefactos generandose ahora mismo, sumando todos los assets."""
        return sum(
            1
            for asset in self._assets.values()
            for artifact in (asset.video, *asset.audio.values())
            if artifact.state is ArtifactState.BUILDING
        )

    def building_asset_ids(self) -> set[str]:
        """Assets con algun build en curso. El GC no puede tocarlos."""
        return {
            asset.id
            for asset in self._assets.values()
            if any(
                artifact.state is ArtifactState.BUILDING
                for artifact in (asset.video, *asset.audio.values())
            )
        }

    def forget(self, asset_id: str) -> None:
        """Saca el asset de memoria tras borrarlo del disco.

        Sin esto, un asset barrido por el GC seguiria figurando como listo y la
        proxima apertura serviria playlists que apuntan a segmentos que ya no
        existen.
        """
        self._assets.pop(asset_id, None)
        self._locks.pop(asset_id, None)

    async def wait_for_builds(self) -> None:
        """Espera a que terminen todos los builds en curso.

        La API no la usa —sirve las playlists en EVENT mientras tanto— pero la
        CLI necesita el archivo completo antes de terminar.
        """
        while True:
            pending = [task for task in self._tasks if not task.done()]
            if not pending:
                return
            await asyncio.gather(*pending, return_exceptions=True)

    def write_playlists(self, asset_id: str) -> Path | None:
        """Deja las playlists como archivos reales, para servir sin la API.

        La API las calcula en cada request; en modo CLI no hay quien las
        calcule, asi que se escriben una vez terminado el build.
        """
        asset = self._assets.get(asset_id)
        if asset is None:
            return None

        master = self.master_playlist(asset_id)
        if master is None:
            return None

        master_path = asset.paths.root / "master.m3u8"
        master_path.write_text(master, encoding="utf-8")

        video = self.media_playlist(asset_id)
        if video is not None:
            (asset.paths.video / "playlist.m3u8").write_text(video, encoding="utf-8")

        for track in asset.audio:
            media = self.media_playlist(asset_id, track=track)
            if media is not None:
                (asset.paths.audio(track) / "playlist.m3u8").write_text(
                    media, encoding="utf-8"
                )

        return master_path

    async def shutdown(self) -> None:
        """Mata todos los FFmpeg en curso y cancela sus tareas."""
        for managed in list(self._processes.values()):
            await managed.kill()
        for task in list(self._tasks):
            task.cancel()
        self._processes.clear()
        log.info("builds detenidos")

    # --- Playlists ----------------------------------------------------------

    def media_playlist(self, asset_id: str, track: int | None = None) -> str | None:
        """Media playlist de la pista pedida (None = video).

        Se calcula con las duraciones que reporto FFmpeg en `internal.m3u8`.
        Mientras ese archivo no tenga `#EXT-X-ENDLIST`, la playlist sale como
        EVENT: el build sigue corriendo y la lista va a crecer.
        """
        asset = self._assets.get(asset_id)
        if asset is None:
            return None

        if track is None:
            directory = asset.paths.video
        elif track in asset.audio:
            directory = asset.paths.audio(track)
        else:
            return None

        text = _read_internal(directory)
        if text is None:
            return None

        return playlist.build_media_playlist(
            playlist.parse_internal_playlist(text),
            complete=playlist.is_complete(text),
        )

    def master_playlist(self, asset_id: str) -> str | None:
        """Master con el video y una rendition de audio por pista."""
        asset = self._assets.get(asset_id)
        if asset is None:
            return None

        return playlist.build_master_playlist(
            video_uri="video/playlist.m3u8",
            renditions=self._renditions(asset),
            bandwidth=asset.info.bit_rate or FALLBACK_BANDWIDTH,
            video_codec=_video_codec(asset),
            resolution=(
                (asset.info.width, asset.info.height)
                if asset.info.width and asset.info.height
                else None
            ),
        )

    def _renditions(self, asset: Asset) -> list[playlist.AudioRendition]:
        names = _unique_names(asset.info.audio_tracks)
        renditions = []

        for track in asset.info.audio_tracks:
            # Solo se declaran las pistas que realmente se generaron.
            if track.index not in asset.audio:
                continue
            copied = audio_copy_allowed(track, asset.options)
            renditions.append(
                playlist.AudioRendition(
                    uri=f"audio/{track.index}/playlist.m3u8",
                    name=names[track.index],
                    language=track.language,
                    channels=track.channels if copied else asset.options.audio_channels,
                    codec=playlist.aac_codec_string(track.profile if copied else None),
                    default=track.index == 0,
                )
            )
        return renditions

    # --- Carga y construccion -----------------------------------------------

    async def _load(self, asset_id: str, source: Path) -> Asset:
        """Arma el asset desde el cache si existe, o lo analiza de cero."""
        paths = self._store.prepare(asset_id)
        cached = self._store.read_manifest(asset_id)

        if cached is not None and "info" in cached:
            info = media_analyzer.from_dict(cached["info"])
            subtitles = {int(k): v for k, v in cached.get("subtitles", {}).items()}
            log.info("asset recuperado del cache", asset_id=asset_id)
        else:
            info = await analyze(source)
            subtitles = await self._extract_subtitles(source, paths, info)

        asset = Asset(
            id=asset_id,
            source=source,
            paths=paths,
            info=info,
            options=self._options,
            subtitles=subtitles,
        )

        asset.video.state = _state_on_disk(paths.video)
        for track in info.audio_tracks:
            if self._audio_tracks is not None and track.index not in self._audio_tracks:
                continue
            asset.audio[track.index] = Artifact(
                state=_state_on_disk(paths.audio(track.index))
            )

        self._save(asset)
        return asset

    async def _extract_subtitles(
        self, source: Path, paths: AssetPaths, info: SourceInfo,
    ) -> dict[int, str]:
        """Extrae cada pista de subtitulos a WebVTT. Devuelve {indice: archivo}.

        Los subtitulos quedan fuera del pipeline de audio a proposito: sus
        tiempos son absolutos respecto del original, igual que los de los
        segmentos, asi que se mantienen sincronizados en cualquier posicion.
        """
        extracted: dict[int, str] = {}

        for track in info.subtitle_tracks:
            name = f"sub_{track.index}_{track.language}.vtt"
            try:
                await extract_subtitle(source, paths.subs / name, track.index)
            except FFmpegError as exc:
                # PGS y VobSub son bitmap: no hay WebVTT posible sin OCR.
                log.warning(
                    "no se pudo extraer subtitulo",
                    track=track.index,
                    codec=track.codec,
                    error=exc.stderr[-200:] if exc.stderr else "",
                )
                continue
            extracted[track.index] = name

        return extracted

    def _start_pending_builds(self, asset: Asset) -> None:
        """Lanza los builds que faltan. No relanza los que ya estan en curso."""
        if asset.video.state is ArtifactState.PENDING:
            asset.video.state = ArtifactState.BUILDING
            _discard_internal(asset.paths.video)
            self._spawn(
                asset,
                VIDEO_KEY,
                asset.video,
                build_video_args(
                    asset.source, asset.paths.video, asset.info, asset.options
                ),
            )

        for index, artifact in asset.audio.items():
            if artifact.state is not ArtifactState.PENDING:
                continue
            artifact.state = ArtifactState.BUILDING
            directory = self._store.prepare_audio(asset.id, index)
            _discard_internal(directory)
            self._spawn(
                asset,
                f"audio:{index}",
                artifact,
                build_audio_args(
                    asset.source, directory, asset.info, asset.options, index
                ),
            )

    def _spawn(
        self, asset: Asset, key: str, artifact: Artifact, args: list[str],
    ) -> None:
        task = asyncio.create_task(self._run(asset, key, artifact, args))
        self._tasks.add(task)
        task.add_done_callback(self._tasks.discard)

    async def _run(
        self, asset: Asset, key: str, artifact: Artifact, args: list[str],
    ) -> None:
        """Corre un FFmpeg y deja el artefacto en su estado final."""
        def on_progress(seconds: float) -> None:
            artifact.seconds_done = seconds
            if self.on_progress is not None:
                self.on_progress(key, seconds)

        async with self._semaphore:
            log.info("build iniciado", asset_id=asset.id, artifact=key)
            try:
                managed = await start_ffmpeg(args, on_progress=on_progress)
                self._processes[(asset.id, key)] = managed
                await managed.wait()
            except FFmpegError as exc:
                artifact.state = ArtifactState.FAILED
                artifact.error = exc.stderr
                log.error(
                    "build fallo",
                    asset_id=asset.id,
                    artifact=key,
                    stderr_tail=exc.stderr[-200:] if exc.stderr else "",
                )
            except asyncio.CancelledError:
                # Shutdown o cambio de archivo: queda para reintentar.
                artifact.state = ArtifactState.PENDING
                raise
            else:
                artifact.state = ArtifactState.READY
                log.info("build completo", asset_id=asset.id, artifact=key)
            finally:
                self._processes.pop((asset.id, key), None)
                self._save(asset)

    async def _wait_until_playable(self, asset: Asset) -> None:
        """Espera a que haya segmentos suficientes para arrancar.

        No espera el build completo: con la playlist en EVENT el player puede
        empezar apenas existen los primeros segmentos.
        """
        elapsed = 0.0
        while elapsed < PLAYABLE_TIMEOUT:
            if asset.video.state is ArtifactState.FAILED:
                return
            if _segment_count(asset.paths.video) >= PLAYABLE_SEGMENTS:
                return
            if asset.video.state is ArtifactState.READY:
                return
            await asyncio.sleep(PLAYABLE_POLL_INTERVAL)
            elapsed += PLAYABLE_POLL_INTERVAL

        log.warning("timeout esperando los primeros segmentos", asset_id=asset.id)

    def _save(self, asset: Asset) -> None:
        self._store.write_manifest(
            asset.id,
            {
                "asset_id": asset.id,
                "source": str(asset.source),
                "duration": asset.info.duration,
                "strategy": asset.info.strategy.value,
                "video": asset.video.state.value,
                "audio": {str(i): a.state.value for i, a in asset.audio.items()},
                "subtitles": {str(i): name for i, name in asset.subtitles.items()},
                "info": media_analyzer.to_dict(asset.info),
            },
        )


def _read_internal(directory: Path) -> str | None:
    """Lee el `internal.m3u8` de FFmpeg. None si todavia no existe.

    FFmpeg lo reescribe a medida que cierra cada segmento, asi que puede
    aparecer y desaparecer por un instante durante el rename.
    """
    try:
        return (directory / playlist.INTERNAL_PLAYLIST).read_text(encoding="utf-8")
    except (FileNotFoundError, OSError):
        return None


def _discard_internal(directory: Path) -> None:
    """Borra el `internal.m3u8` de un build que quedo a medias.

    Sin esto, el playlist viejo haria pasar por reproducibles unos segmentos
    que FFmpeg esta por reescribir, y el cliente arrancaria sobre datos que
    estan a punto de cambiar debajo suyo.
    """
    (directory / playlist.INTERNAL_PLAYLIST).unlink(missing_ok=True)


def _segment_count(directory: Path) -> int:
    text = _read_internal(directory)
    return len(playlist.parse_internal_playlist(text)) if text else 0


def _state_on_disk(directory: Path) -> ArtifactState:
    """READY solo si FFmpeg cerro la playlist con `#EXT-X-ENDLIST`.

    Un build interrumpido deja el `internal.m3u8` sin cerrar: se rehace desde
    cero, que es lo correcto y ademas barato de detectar.
    """
    text = _read_internal(directory)
    if text is not None and playlist.is_complete(text):
        return ArtifactState.READY
    return ArtifactState.PENDING


def _video_codec(asset: Asset) -> str | None:
    if video_copy_allowed(asset.info, asset.options):
        return playlist.avc_codec_string(
            asset.info.video_profile, asset.info.video_level
        )
    return TRANSCODED_VIDEO_CODEC


def _unique_names(tracks: tuple[AudioTrack, ...]) -> dict[int, str]:
    """Nombre visible de cada rendition, sin repetidos.

    Dos pistas con el mismo idioma y sin titulo darian el mismo NAME, y el
    selector del player quedaria ambiguo.
    """
    names: dict[int, str] = {}
    seen: dict[str, int] = {}

    for track in tracks:
        base = track.title.strip() or track.language
        count = seen.get(base, 0)
        seen[base] = count + 1
        names[track.index] = base if count == 0 else f"{base} ({count + 1})"

    return names
