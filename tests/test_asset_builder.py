"""Tests de asset_builder: FFmpeg mockeado, nada toca los binarios reales."""

from __future__ import annotations

import asyncio
from pathlib import Path

import pytest

from app.errors import FFmpegError
from app.services import asset_builder as builder_module
from app.services.asset_builder import ArtifactState, AssetBuilder
from app.services.asset_store import AssetStore, asset_id_for
from app.services.media_analyzer import AudioTrack
from app.services.transcoder import TranscodeOptions


@pytest.fixture
def source(tmp_path: Path) -> Path:
    path = tmp_path / "pelicula.mkv"
    path.write_bytes(b"contenido de la pelicula")
    return path


@pytest.fixture
def store(tmp_path: Path) -> AssetStore:
    return AssetStore(root=tmp_path / "cache")


def make_builder(store: AssetStore, **options) -> AssetBuilder:
    return AssetBuilder(store=store, options=TranscodeOptions(**options))


# --- Apertura y construccion ------------------------------------------------

async def test_open_lanza_un_build_por_pista(patched, store, source, hevc_ac3):
    spy = patched(info=hevc_ac3)

    asset = await make_builder(store).open(source)

    # Un video mas una pista de audio por idioma.
    assert len(spy.calls) == 3
    assert asset.status == "ready"


async def test_el_video_y_el_audio_van_a_directorios_separados(
    patched, store, source, hevc_ac3,
):
    spy = patched(info=hevc_ac3)

    await make_builder(store).open(source)
    outputs = [Path(o) for o in spy.outputs]

    assert any(o.parent.name == "video" for o in outputs)
    assert {o.parent.name for o in outputs if o.parent.parent.name == "audio"} == {"0", "1"}


async def test_open_extrae_los_subtitulos_una_vez(patched, store, source, hevc_ac3):
    patched(info=hevc_ac3)

    asset = await make_builder(store).open(source)

    assert asset.subtitles == {0: "sub_0_eng.vtt"}
    assert (asset.paths.subs / "sub_0_eng.vtt").exists()


async def test_dos_open_concurrentes_no_duplican_ffmpeg(patched, store, source):
    spy = patched()
    builder = make_builder(store)

    await asyncio.gather(builder.open(source), builder.open(source))

    # Un video y una pista de audio, no dos de cada uno.
    assert len(spy.calls) == 2


async def test_reabrir_el_asset_no_reconstruye(patched, store, source):
    spy = patched()
    builder = make_builder(store)

    await builder.open(source)
    await builder.open(source)

    assert len(spy.calls) == 2


async def test_el_cache_evita_rehacer_el_trabajo(patched, store, source):
    # Primera apertura: construye todo y deja el cache listo.
    patched()
    await make_builder(store).open(source)

    # Segunda apertura en un proceso nuevo: no debe correr FFmpeg ni ffprobe.
    spy = patched()

    async def explode(path, audio_track=0):
        raise AssertionError("no deberia volver a analizar el archivo")

    builder_module.analyze = explode
    asset = await make_builder(store).open(source)

    assert spy.calls == []
    assert asset.status == "ready"


async def test_un_build_interrumpido_se_rehace(patched, store, source):
    # Sin #EXT-X-ENDLIST el artefacto quedo a medias y hay que regenerarlo.
    spy = patched(complete=False)
    await make_builder(store).open(source)

    spy2 = patched(complete=False)
    await make_builder(store).open(source)

    assert len(spy2.calls) == 2


# --- Errores ----------------------------------------------------------------

async def test_una_pista_que_falla_no_arrastra_a_las_demas(
    patched, store, source, hevc_ac3,
):
    spy = patched(info=hevc_ac3, fail_on=("0:a:1",))

    asset = await make_builder(store).open(source)
    await asyncio.sleep(0.05)

    assert asset.video.state is ArtifactState.READY
    assert asset.audio[0].state is ArtifactState.READY
    assert asset.audio[1].state is ArtifactState.FAILED
    assert asset.status == "processing"
    assert "algo exploto" in asset.error
    assert len(spy.calls) == 3


async def test_si_falla_el_video_el_asset_queda_failed(patched, store, source):
    patched(fail_on=("0:v:0",))

    asset = await make_builder(store).open(source)

    assert asset.status == "failed"
    assert "algo exploto" in asset.error


async def test_subtitulo_bitmap_no_rompe_la_apertura(
    monkeypatch, patched, store, source, hevc_ac3,
):
    patched(info=hevc_ac3)

    async def fail_extract(source, output_path, track):
        raise FFmpegError("ffmpeg", 1, "PGS no se puede convertir a webvtt")

    monkeypatch.setattr(builder_module, "extract_subtitle", fail_extract)

    asset = await make_builder(store).open(source)

    assert asset.subtitles == {}
    assert asset.status == "ready"


# --- Playlists --------------------------------------------------------------

async def test_master_declara_una_rendition_por_pista(
    patched, store, source, hevc_ac3,
):
    patched(info=hevc_ac3)
    builder = make_builder(store)
    asset = await builder.open(source)

    master = builder.master_playlist(asset.id)

    assert master.count("#EXT-X-MEDIA:") == 2
    assert 'URI="audio/0/playlist.m3u8"' in master
    assert 'URI="audio/1/playlist.m3u8"' in master
    assert master.strip().endswith("video/playlist.m3u8")


async def test_master_usa_el_codec_real_cuando_copia_el_video(
    patched, store, source, h264_aac,
):
    from dataclasses import replace

    info = replace(h264_aac, video_profile="High", video_level=41)
    patched(info=info)
    builder = make_builder(store)
    asset = await builder.open(source)

    assert 'CODECS="avc1.640029,mp4a.40.2"' in builder.master_playlist(asset.id)


async def test_master_declara_el_codec_fijo_cuando_recodifica(
    patched, store, source, hevc_ac3,
):
    patched(info=hevc_ac3)
    builder = make_builder(store)
    asset = await builder.open(source)

    # El video re-codificado sale siempre high@4.1 por construccion.
    assert "avc1.640029" in builder.master_playlist(asset.id)


async def test_media_playlist_de_video_y_de_cada_audio(
    patched, store, source, hevc_ac3,
):
    patched(info=hevc_ac3)
    builder = make_builder(store)
    asset = await builder.open(source)

    video = builder.media_playlist(asset.id)
    audio_0 = builder.media_playlist(asset.id, track=0)
    audio_1 = builder.media_playlist(asset.id, track=1)

    assert "#EXT-X-PLAYLIST-TYPE:VOD" in video
    # I4: el timeline no cambia al cambiar de idioma.
    assert audio_0 == audio_1 == video


async def test_media_playlist_es_event_mientras_construye(patched, store, source):
    patched(complete=False)
    builder = make_builder(store)
    asset = await builder.open(source)

    media = builder.media_playlist(asset.id)

    assert "#EXT-X-PLAYLIST-TYPE:EVENT" in media
    assert "#EXT-X-ENDLIST" not in media


async def test_playlists_de_un_asset_desconocido(store):
    builder = make_builder(store)

    assert builder.master_playlist("no-existe") is None
    assert builder.media_playlist("no-existe") is None


async def test_media_playlist_de_una_pista_inexistente(patched, store, source):
    patched()
    builder = make_builder(store)
    asset = await builder.open(source)

    assert builder.media_playlist(asset.id, track=7) is None


# --- Nombres de rendition ---------------------------------------------------

def test_nombres_de_rendition_no_se_repiten():
    tracks = (
        AudioTrack(index=0, codec="aac", channels=2, language="spa", title=""),
        AudioTrack(index=1, codec="aac", channels=2, language="spa", title=""),
        AudioTrack(index=2, codec="aac", channels=2, language="eng", title="Comentario"),
    )

    assert builder_module._unique_names(tracks) == {
        0: "spa", 1: "spa (2)", 2: "Comentario",
    }


# --- Estado y progreso ------------------------------------------------------

async def test_progreso_se_calcula_sobre_la_duracion(patched, store, source):
    patched()
    builder = make_builder(store)
    asset = await builder.open(source)
    asset.video.state = ArtifactState.BUILDING  # simula el build todavia en curso

    # El fake reporta 18s procesados de los 100s del fixture.
    assert asset.video.seconds_done == 18.0
    assert asset.progress == pytest.approx(0.18)


async def test_progreso_completo_cuando_termina(patched, store, source):
    patched()
    builder = make_builder(store)
    asset = await builder.open(source)

    assert asset.progress == 1.0


async def test_shutdown_mata_los_procesos_en_curso(patched, store, source):
    spy = patched()
    builder = make_builder(store)
    asset = await builder.open(source)
    asset.video.state = ArtifactState.PENDING

    # Un build largo que no termina solo.
    started = asyncio.Event()

    class Blocking:
        async def wait(self):
            started.set()
            await asyncio.Event().wait()

        async def kill(self):
            spy.kills += 1

        @property
        def is_running(self):
            return True

    async def blocking_start(args, on_progress=None):
        return Blocking()

    builder_module.start_ffmpeg = blocking_start
    builder._start_pending_builds(asset)
    await started.wait()

    await builder.shutdown()

    assert spy.kills == 1


async def test_active_ids_lista_los_assets_abiertos(patched, store, source):
    patched()
    builder = make_builder(store)
    asset = await builder.open(source)

    assert builder.active_ids() == {asset.id}
    assert asset.id == asset_id_for(source)
