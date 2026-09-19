"""Tests de asset_builder: FFmpeg mockeado, nada toca los binarios reales."""

from __future__ import annotations

import asyncio
from dataclasses import replace
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


async def build_all(store: AssetStore, source: Path, **options):
    """Abre el asset y espera a que todos los builds terminen."""
    builder = make_builder(store, **options)
    asset = await builder.open(source)
    await builder.wait_for_builds()
    return builder, asset


# --- Apertura ---------------------------------------------------------------

async def test_open_lanza_un_build_por_artefacto(patched, store, source, hevc_ac3):
    spy = patched(info=hevc_ac3)

    _, asset = await build_all(store, source)

    # Video, dos pistas de audio y un subtitulo.
    assert len(spy.calls) == 4
    assert asset.status == "ready"


async def test_open_no_espera_a_que_termine_nada(patched, store, source, hevc_ac3):
    # El POST tiene que volver enseguida: antes esperaba los subtitulos, que en
    # un archivo grande son varios minutos.
    patched(info=hevc_ac3)
    builder = make_builder(store)

    asset = await builder.open(source)

    assert asset.status == "processing"
    assert asset.video.state is ArtifactState.BUILDING

    await builder.wait_for_builds()
    assert asset.status == "ready"


async def test_el_video_se_lanza_antes_que_los_subtitulos(
    patched, store, source, hevc_ac3,
):
    # Cada .vtt obliga a FFmpeg a leer el archivo entero; el video es lo unico
    # que el usuario esta esperando para poder mirar algo.
    spy = patched(info=hevc_ac3)

    await build_all(store, source)
    orden = [Path(call[-1]) for call in spy.calls]
    subtitulos = [i for i, p in enumerate(orden) if p.suffix == ".vtt"]

    assert orden[0].parent.name == "video"
    assert min(subtitulos) > 0


async def test_el_video_y_el_audio_van_a_directorios_separados(
    patched, store, source, hevc_ac3,
):
    spy = patched(info=hevc_ac3)

    await build_all(store, source)
    salidas = [Path(o) for o in spy.outputs]

    assert any(o.parent.name == "video" for o in salidas)
    assert {o.parent.name for o in salidas if o.parent.parent.name == "audio"} == {"0", "1"}


async def test_los_subtitulos_se_extraen_una_vez(patched, store, source, hevc_ac3):
    patched(info=hevc_ac3)

    _, asset = await build_all(store, source)

    assert asset.ready_subtitles() == {0: "sub_0_eng.vtt"}
    assert (asset.paths.subs / "sub_0_eng.vtt").exists()


async def test_dos_open_concurrentes_no_duplican_ffmpeg(patched, store, source):
    spy = patched()
    builder = make_builder(store)

    await asyncio.gather(builder.open(source), builder.open(source))
    await builder.wait_for_builds()

    # Un video y una pista de audio, no dos de cada uno.
    assert len(spy.calls) == 2


async def test_reabrir_el_asset_no_reconstruye(patched, store, source):
    spy = patched()
    builder = make_builder(store)

    await builder.open(source)
    await builder.wait_for_builds()
    await builder.open(source)
    await builder.wait_for_builds()

    assert len(spy.calls) == 2


async def test_el_cache_evita_rehacer_el_trabajo(patched, store, source):
    patched()
    await build_all(store, source)

    # Segunda apertura en un proceso nuevo: no debe correr FFmpeg ni ffprobe.
    spy = patched()

    async def explode(path, audio_track=0):
        raise AssertionError("no deberia volver a analizar el archivo")

    builder_module.analyze = explode
    _, asset = await build_all(store, source)

    assert spy.calls == []
    assert asset.status == "ready"


async def test_un_build_interrumpido_se_rehace(patched, store, source):
    # Sin #EXT-X-ENDLIST el artefacto quedo a medias y hay que regenerarlo.
    patched(complete=False)
    await build_all(store, source)

    spy2 = patched(complete=False)
    await build_all(store, source)

    assert len(spy2.calls) == 2


# --- Playable ---------------------------------------------------------------

async def test_no_es_playable_sin_segmentos(patched, store, source):
    patched()
    builder = make_builder(store)

    asset = await builder.open(source)

    assert asset.playable is False
    await builder.wait_for_builds()


async def test_es_playable_con_segmentos_suficientes(patched, store, source):
    patched(segments=builder_module.PLAYABLE_SEGMENTS, complete=False)

    _, asset = await build_all(store, source)
    asset.video.state = ArtifactState.BUILDING  # el build sigue en curso

    assert asset.playable is True


async def test_no_es_playable_con_un_solo_segmento(patched, store, source):
    patched(segments=1, complete=False)

    _, asset = await build_all(store, source)
    asset.video.state = ArtifactState.BUILDING

    assert asset.playable is False


async def test_un_video_terminado_siempre_es_playable(patched, store, source):
    patched(segments=1)

    _, asset = await build_all(store, source)

    assert asset.video.state is ArtifactState.READY
    assert asset.playable is True


async def test_playable_cuenta_archivos_no_la_playlist(patched, store, source):
    # FFmpeg escribe los .m4s mucho antes que su playlist. Contar la playlist
    # hacia parecer que no habia nada cuando ya habia cientos de segmentos.
    patched()
    _, asset = await build_all(store, source)
    (asset.paths.video / "internal.m3u8").unlink()
    asset.video.state = ArtifactState.BUILDING

    assert asset.playable is True


# --- Errores ----------------------------------------------------------------

async def test_una_pista_que_falla_no_arrastra_a_las_demas(
    patched, store, source, hevc_ac3,
):
    spy = patched(info=hevc_ac3, fail_on=("0:a:1",))

    _, asset = await build_all(store, source)

    assert asset.video.state is ArtifactState.READY
    assert asset.audio[0].state is ArtifactState.READY
    assert asset.audio[1].state is ArtifactState.FAILED
    assert asset.status == "processing"
    assert "algo exploto" in asset.error
    assert len(spy.calls) == 4


async def test_si_falla_el_video_el_asset_queda_failed(patched, store, source):
    patched(fail_on=("0:v:0",))

    _, asset = await build_all(store, source)

    assert asset.status == "failed"
    assert "algo exploto" in asset.error


async def test_un_subtitulo_que_falla_no_afecta_al_asset(
    patched, store, source, hevc_ac3,
):
    # PGS y VobSub son bitmap: no hay WebVTT posible. El asset igual sirve.
    patched(info=hevc_ac3, fail_on=("0:s:0",))

    _, asset = await build_all(store, source)

    assert asset.status == "ready"
    assert asset.error is None
    assert asset.ready_subtitles() == {}
    assert asset.failed_subtitles() == [0]


async def test_un_subtitulo_fallido_no_se_reintenta(patched, store, source, hevc_ac3):
    patched(info=hevc_ac3, fail_on=("0:s:0",))
    await build_all(store, source)

    spy = patched(info=hevc_ac3, fail_on=("0:s:0",))
    await build_all(store, source)

    assert not any(Path(call[-1]).suffix == ".vtt" for call in spy.calls)


# --- Playlists --------------------------------------------------------------

async def test_master_declara_una_rendition_por_pista(
    patched, store, source, hevc_ac3,
):
    patched(info=hevc_ac3)
    builder, asset = await build_all(store, source)

    master = builder.master_playlist(asset.id)

    assert master.count("#EXT-X-MEDIA:") == 2
    assert 'URI="audio/0/playlist.m3u8"' in master
    assert 'URI="audio/1/playlist.m3u8"' in master
    assert master.strip().endswith("video/playlist.m3u8")


async def test_master_usa_el_codec_real_cuando_copia_el_video(
    patched, store, source, h264_aac,
):
    patched(info=replace(h264_aac, video_profile="High", video_level=41))
    builder, asset = await build_all(store, source)

    assert 'CODECS="avc1.640029,mp4a.40.2"' in builder.master_playlist(asset.id)


async def test_master_declara_el_codec_fijo_cuando_recodifica(
    patched, store, source, hevc_ac3,
):
    patched(info=hevc_ac3)
    builder, asset = await build_all(store, source)

    # El video re-codificado sale siempre high@4.1 por construccion.
    assert "avc1.640029" in builder.master_playlist(asset.id)


async def test_media_playlist_de_video_y_de_cada_audio(
    patched, store, source, hevc_ac3,
):
    patched(info=hevc_ac3)
    builder, asset = await build_all(store, source)

    video = builder.media_playlist(asset.id)
    audio_0 = builder.media_playlist(asset.id, track=0)
    audio_1 = builder.media_playlist(asset.id, track=1)

    assert "#EXT-X-PLAYLIST-TYPE:VOD" in video
    # I4: el timeline no cambia al cambiar de idioma.
    assert audio_0 == audio_1 == video


async def test_media_playlist_es_event_mientras_construye(patched, store, source):
    patched(complete=False)
    builder, asset = await build_all(store, source)

    media = builder.media_playlist(asset.id)

    assert "#EXT-X-PLAYLIST-TYPE:EVENT" in media
    assert "#EXT-X-ENDLIST" not in media


async def test_playlists_de_un_asset_desconocido(store):
    builder = make_builder(store)

    assert builder.master_playlist("no-existe") is None
    assert builder.media_playlist("no-existe") is None


async def test_media_playlist_de_una_pista_inexistente(patched, store, source):
    patched()
    builder, asset = await build_all(store, source)

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
    _, asset = await build_all(store, source)
    asset.video.state = ArtifactState.BUILDING  # simula el build todavia en curso

    # El fake reporta 18s procesados de los 100s del fixture.
    assert asset.video.seconds_done == 18.0
    assert asset.progress == pytest.approx(0.18)


async def test_progreso_completo_cuando_termina(patched, store, source):
    patched()
    _, asset = await build_all(store, source)

    assert asset.progress == 1.0


async def test_shutdown_mata_los_procesos_en_curso(patched, store, source):
    spy = patched()
    builder = make_builder(store)
    asset = await builder.open(source)
    await builder.wait_for_builds()
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
    builder, asset = await build_all(store, source)

    assert builder.active_ids() == {asset.id}
    assert asset.id == asset_id_for(source)


# --- Seleccion de pistas ----------------------------------------------------
#
# El flag `ignore` lo guarda la ficha; el builder lo recibe como dos conjuntos
# de indices. Una pista ignorada no se registra, y entonces no existe para el
# resto del sistema: ni se genera, ni se declara en el master, ni sale en la
# respuesta al player.

def _mapeadas(spy) -> set[str]:
    """Los `-map` de cada FFmpeg lanzado: identifica que pista genero cada uno."""
    return {
        call[call.index("-map") + 1] for call in spy.calls if "-map" in call
    }


async def test_una_pista_ignorada_no_se_genera(patched, store, source, hevc_ac3):
    spy = patched(info=hevc_ac3)
    builder = make_builder(store)

    asset = await builder.open(
        source, ignored_audio={1}, ignored_subtitles={0}
    )
    await builder.wait_for_builds()

    assert _mapeadas(spy) == {"0:v:0", "0:a:0"}
    assert set(asset.audio) == {0}
    assert asset.subtitles == {}


async def test_el_master_no_declara_una_pista_ignorada(
    patched, store, source, hevc_ac3
):
    patched(info=hevc_ac3)
    builder = make_builder(store)

    asset = await builder.open(source, ignored_audio={1})
    await builder.wait_for_builds()

    assert builder.master_playlist(asset.id).count("#EXT-X-MEDIA:") == 1


async def test_reabrir_sin_seleccion_genera_todo(patched, store, source, hevc_ac3):
    """Regla B: no pasar seleccion es "genera todo", no "dejar como estaba".

    La ficha es la unica fuente de la decision; el `ignore` que quedo escrito
    en el manifest es una copia derivada y no manda. Por eso abrir por ruta
    —sin ficha— regenera el archivo completo.
    """
    spy = patched(info=hevc_ac3)
    builder = make_builder(store)

    await builder.open(source, ignored_audio={1}, ignored_subtitles={0})
    await builder.wait_for_builds()
    assert _mapeadas(spy) == {"0:v:0", "0:a:0"}

    asset = await builder.open(source)
    await builder.wait_for_builds()

    assert _mapeadas(spy) == {"0:v:0", "0:a:0", "0:a:1", "0:s:0"}
    assert set(asset.audio) == {0, 1}


async def test_cambiar_la_seleccion_de_un_asset_ya_abierto(
    patched, store, source, hevc_ac3
):
    """El caso que falla mudo si el filtro vuelve a `_load`.

    `_load` solo corre con el cache frio: si la seleccion se aplicara ahi, la
    segunda apertura devolveria el asset memorizado sin enterarse del cambio.
    """
    spy = patched(info=hevc_ac3)
    builder = make_builder(store)

    await builder.open(source, ignored_audio={0, 1})
    await builder.wait_for_builds()
    assert "0:a:0" not in _mapeadas(spy)

    asset = await builder.open(source, ignored_audio={1})
    await builder.wait_for_builds()

    assert "0:a:0" in _mapeadas(spy)
    assert set(asset.audio) == {0}


async def test_ignorar_una_pista_ya_generada_la_saca_del_master(
    patched, store, source, hevc_ac3
):
    """No borra los segmentos —de eso se ocupa el GC— pero deja de declararla."""
    patched(info=hevc_ac3)
    builder = make_builder(store)

    asset = await builder.open(source)
    await builder.wait_for_builds()
    assert set(asset.audio) == {0, 1}

    asset = await builder.open(source, ignored_audio={1})

    assert set(asset.audio) == {0}
    assert builder.master_playlist(asset.id).count("#EXT-X-MEDIA:") == 1
