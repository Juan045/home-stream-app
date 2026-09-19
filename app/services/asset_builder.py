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
from dataclasses import asdict, dataclass, field
from enum import Enum
from pathlib import Path
from typing import Callable

import structlog

from app import codecs
from app.errors import FFmpegError
from app.services import media_analyzer, playlist
from app.services.asset_store import AssetPaths, AssetStore, asset_id_for
from app.services.media_analyzer import AudioTrack, SourceInfo, analyze
from app.services.transcoder import (
    ManagedFFmpeg,
    TranscodeOptions,
    audio_copy_allowed,
    build_audio_args,
    build_subtitle_args,
    build_video_args,
    options_from_dict,
    start_ffmpeg,
    video_copy_allowed,
)

log = structlog.get_logger("asset_builder")

VIDEO_KEY = "video"

# Segmentos que tienen que existir para que el player pueda arrancar. Con uno
# solo alcanza para reproducir, pero dos le dan margen al reload de la playlist
# EVENT y evitan un stall inmediato.
PLAYABLE_SEGMENTS = 2

# Si ffprobe no reporta bitrate, hay que declarar algo en el master.
FALLBACK_BANDWIDTH = 4_000_000


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
    # Nombre del .vtt planeado para cada pista, este listo o no.
    subtitles: dict[int, str] = field(default_factory=dict)
    subtitle_artifacts: dict[int, Artifact] = field(default_factory=dict)
    # Lo que recordaba el manifest al abrir: que .vtt ya estaban extraidos y
    # cuales fallaron. Hace falta despues de `_load` porque una pista puede
    # entrar a la seleccion mas tarde y hay que saber en que estado arranca.
    # Sin la memoria de los fallidos se reintentaria extraer PGS en cada
    # apertura, que es justo lo que no anda nunca.
    extracted_subtitles: dict[int, str] = field(default_factory=dict)
    failed_subtitles_cached: set[int] = field(default_factory=set)
    # True si el asset lo genero una codificacion de biblioteca. El GC no lo
    # toca: nadie esta mirando una pelicula que se codifica de noche, asi que
    # el LRU se comeria seis horas de CPU sin que nadie se entere.
    pinned: bool = False

    @property
    def playable(self) -> bool:
        """True si ya hay segmentos suficientes para empezar a reproducir.

        Se cuentan los archivos en disco, no las entradas de la playlist de
        FFmpeg: esa la escribe cuando quiere, y esperar por ella hacia parecer
        que no habia nada cuando en realidad ya habia cientos de segmentos.
        """
        if self.video.state is ArtifactState.READY:
            return True
        return _segment_count(self.paths.video) >= PLAYABLE_SEGMENTS

    def ready_subtitles(self) -> dict[int, str]:
        """Solo las pistas efectivamente extraidas."""
        return {
            index: name
            for index, name in self.subtitles.items()
            if self.subtitle_artifacts.get(index, Artifact()).state
            is ArtifactState.READY
        }

    def all_artifacts(self) -> list[Artifact]:
        return [
            self.video,
            *self.audio.values(),
            *self.subtitle_artifacts.values(),
        ]

    def failed_subtitles(self) -> list[int]:
        return sorted(
            index
            for index, artifact in self.subtitle_artifacts.items()
            if artifact.state is ArtifactState.FAILED
        )

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

    async def open(
        self,
        source: Path,
        *,
        ignored_audio: set[int] | None = None,
        ignored_subtitles: set[int] | None = None,
        options: TranscodeOptions | None = None,
        pin: bool = False,
    ) -> Asset:
        """Devuelve el asset del archivo, construyendo lo que falte.

        Es idempotente: abrir dos veces la misma pelicula no lanza dos FFmpeg,
        y si el cache ya la tiene completa no lanza ninguno.

        Los dos conjuntos son los indices que la ficha marco para *no* generar.
        No pasarlos significa **generar todo**, no "dejar como estaba": la ficha
        es la unica fuente de esa decision y la copia que queda en el manifest
        es derivada. Por eso abrir por ruta —sin ficha— siempre genera el
        archivo completo, aunque una apertura anterior haya ignorado pistas.

        `options` es el perfil con el que **crear** el asset — lo usa la
        codificacion de biblioteca para pedir AV1 en vez del encoder del server.
        No pisa nada: un asset que ya existe conserva las opciones con las que
        se construyo, porque son las que describen los segmentos que hay en
        disco. Recodificar con otro perfil es borrar el asset primero.

        `pin` lo protege del GC. Ver `Asset.pinned`.
        """
        asset_id = asset_id_for(source)
        lock = self._locks.setdefault(asset_id, asyncio.Lock())

        async with lock:
            asset = self._assets.get(asset_id)
            if asset is None:
                asset = await self._load(asset_id, source, options)
                self._assets[asset_id] = asset
            asset.pinned = asset.pinned or pin

            # Va en `open` y no en `_load` a proposito: `_load` solo corre con
            # el cache frio, asi que si el filtro viviera ahi, cambiar la
            # seleccion de una pelicula ya abierta no haria nada — y sin error.
            asset.info = media_analyzer.with_ignored(
                asset.info,
                audio=ignored_audio or (),
                subtitles=ignored_subtitles or (),
            )
            self._select_tracks(asset)
            self._save(asset)

            self._store.touch(asset_id)
            self._start_pending_builds(asset)

        # No se espera a nada: el cliente consulta `playable` y arranca cuando
        # hay con que. Bloquear aca solo servia para demorar la respuesta.
        return asset

    def get(self, asset_id: str) -> Asset | None:
        return self._assets.get(asset_id)

    def active_ids(self) -> set[str]:
        return set(self._assets)

    @property
    def options(self) -> TranscodeOptions:
        """Las opciones con las que se crean los assets nuevos.

        La expone para que un perfil pueda derivarse de ellas
        (`replace(builder.options, **codecs.ARCHIVE)`) y herede lo que el perfil
        no fija: la duracion del segmento y el audio salen igual de la
        configuracion del server.
        """
        return self._options

    def building_count(self) -> int:
        """Artefactos generandose ahora mismo, sumando todos los assets.

        Los subtitulos cuentan: son procesos de FFmpeg reales leyendo el mismo
        archivo, aunque no bloqueen la reproduccion.
        """
        return sum(
            1
            for asset in self._assets.values()
            for artifact in asset.all_artifacts()
            if artifact.state is ArtifactState.BUILDING
        )

    def building_asset_ids(self) -> set[str]:
        """Assets con algun build en curso. El GC no puede tocarlos."""
        return {
            asset.id
            for asset in self._assets.values()
            if any(
                artifact.state is ArtifactState.BUILDING
                for artifact in asset.all_artifacts()
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

    async def _load(
        self, asset_id: str, source: Path, options: TranscodeOptions | None = None,
    ) -> Asset:
        """Arma el asset desde el cache si existe, o lo analiza de cero.

        **Las opciones salen del manifest, no de la configuracion del server.**
        Son las que describen los segmentos que hay en disco: de ellas dependen
        el CODECS del master y la decision de copiar o recodificar. Tomarlas de
        `self._options` hacia que un asset codificado en AV1 se anunciara con el
        encoder configurado en ese momento — `avc1` — y el player abriera el
        SourceBuffer con un codec que no es el de los segmentos.

        Un manifest sin el bloque (los que se generaron antes de que existiera)
        cae en `options`, o en la configuracion del server si no vino ninguna.
        """
        paths = self._store.prepare(asset_id)
        cached = self._store.read_manifest(asset_id)
        default = options or self._options
        pinned = False

        if cached is not None and "info" in cached:
            info = media_analyzer.from_dict(cached["info"])
            extracted = {int(k): v for k, v in cached.get("subtitles", {}).items()}
            failed = {int(i) for i in cached.get("subtitles_failed", [])}
            default = options_from_dict(cached.get("options", {}), default)
            pinned = bool(cached.get("pinned", False))
            log.info(
                "asset recuperado del cache",
                asset_id=asset_id,
                video_codec=default.video_codec,
            )
        else:
            info = await analyze(source)
            extracted, failed = {}, set()

        asset = Asset(
            id=asset_id,
            source=source,
            paths=paths,
            info=info,
            options=default,
            extracted_subtitles=extracted,
            failed_subtitles_cached=failed,
            pinned=pinned,
        )

        asset.video.state = _state_on_disk(paths.video)
        return asset

    def _select_tracks(self, asset: Asset) -> None:
        """Registra un artefacto por cada pista que haya que generar.

        Ese registro *es* la decision: `_start_pending_builds` recorre lo que
        quede en `asset.audio` y `asset.subtitle_artifacts`, y el master declara
        solo eso. Una pista con `ignore` no se registra y entonces no existe
        para el resto del sistema.

        Se llama en cada `open`, asi que ampliar o achicar la seleccion de una
        pelicula ya abierta funciona. Sacar una pista que ya se genero no borra
        sus segmentos: dejan de declararse y se los lleva el GC con el asset.
        """
        wanted_audio = {
            track.index
            for track in asset.info.audio_tracks
            if not track.ignore
            # El filtro del CLI (`--audio-tracks`) es otra cosa: un "solo
            # estas" global del proceso, no la decision por pista de la ficha.
            and (self._audio_tracks is None or track.index in self._audio_tracks)
        }

        for index in set(asset.audio) - wanted_audio:
            del asset.audio[index]

        for index in wanted_audio - set(asset.audio):
            asset.audio[index] = Artifact(
                state=_state_on_disk(asset.paths.audio(index))
            )

        wanted_subs = {
            track.index
            for track in asset.info.subtitle_tracks
            if not track.ignore
        }

        for index in set(asset.subtitles) - wanted_subs:
            del asset.subtitles[index]
            asset.subtitle_artifacts.pop(index, None)

        for track in asset.info.subtitle_tracks:
            if track.index not in wanted_subs or track.index in asset.subtitles:
                continue
            name = f"sub_{track.index}_{track.language}.vtt"
            asset.subtitles[track.index] = name
            asset.subtitle_artifacts[track.index] = Artifact(
                state=_subtitle_state(
                    asset.paths.subs / name,
                    track.index,
                    asset.extracted_subtitles,
                    asset.failed_subtitles_cached,
                )
            )

    def _start_pending_builds(self, asset: Asset) -> None:
        """Lanza los builds que faltan. No relanza los que ya estan en curso.

        El orden importa: video, audio y recien despues subtitulos. Las tareas
        compiten por el mismo semaforo leyendo el mismo archivo, y el video es
        el unico que el usuario esta esperando para poder mirar algo.
        """
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

        # Ultimos: cada .vtt obliga a FFmpeg a leer el archivo entero, y no
        # tienen ninguna relacion con el video. Antes corrian en serie y antes
        # que todo lo demas, lo que demoraba el arranque varios minutos.
        for index, artifact in asset.subtitle_artifacts.items():
            if artifact.state is not ArtifactState.PENDING:
                continue
            artifact.state = ArtifactState.BUILDING
            output = asset.paths.subs / asset.subtitles[index]
            output.parent.mkdir(parents=True, exist_ok=True)
            self._spawn(
                asset,
                f"sub:{index}",
                artifact,
                build_subtitle_args(asset.source, output, index),
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
                "subtitles": {
                    str(i): name for i, name in asset.ready_subtitles().items()
                },
                # Las pistas bitmap (PGS, VobSub) nunca van a andar: recordarlo
                # evita reintentar la extraccion en cada apertura.
                "subtitles_failed": asset.failed_subtitles(),
                "info": media_analyzer.to_dict(asset.info),
                # Con que se construyo esto. Lo lee `_load` en la proxima
                # apertura: sin este bloque no hay forma de saber que codec
                # tienen los segmentos, y el master los anunciaria con el
                # encoder que el server tenga configurado ese dia.
                "options": asdict(asset.options),
                "pinned": asset.pinned,
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
    """Segmentos escritos, contados en disco.

    A proposito no se parsea el `internal.m3u8`: FFmpeg lo reescribe cuando
    cierra un segmento, pero los archivos aparecen antes. Contar la playlist
    hacia parecer que no habia nada cuando en realidad ya habia cientos.
    """
    if not directory.is_dir():
        return 0
    return sum(1 for _ in directory.glob("seg-*.m4s"))


def _subtitle_state(
    path: Path, index: int, extracted: dict[int, str], failed: set[int],
) -> ArtifactState:
    """Estado de una pista de subtitulos segun el cache."""
    if index in extracted and path.exists():
        return ArtifactState.READY
    if index in failed:
        return ArtifactState.FAILED
    return ArtifactState.PENDING


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
    """CODECS del video en el master: el del origen si se copio, si no el del
    encoder con el que se codifico (`app.codecs`)."""
    if video_copy_allowed(asset.info, asset.options):
        return playlist.avc_codec_string(
            asset.info.video_profile, asset.info.video_level
        )
    return codecs.video(asset.options.video_codec).codec_string


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
