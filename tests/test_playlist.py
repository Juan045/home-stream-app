"""Tests de playlist: logica pura, no toca disco ni FFmpeg."""

from __future__ import annotations

from app.services.playlist import (
    AudioRendition,
    SEGMENT_PATTERN,
    SEGMENT_TEMPLATE,
    aac_codec_string,
    avc_codec_string,
    build_master_playlist,
    build_media_playlist,
    is_complete,
    parse_internal_playlist,
    target_duration,
)

INTERNAL = """#EXTM3U
#EXT-X-VERSION:7
#EXT-X-TARGETDURATION:6
#EXT-X-MEDIA-SEQUENCE:0
#EXT-X-MAP:URI="init.mp4"
#EXTINF:6.000000,
seg-00000.m4s
#EXTINF:6.006000,
seg-00001.m4s
#EXTINF:2.500000,
seg-00002.m4s
#EXT-X-ENDLIST
"""


def lines_of(playlist: str) -> list[str]:
    return playlist.strip().splitlines()


# --- Parseo del internal.m3u8 de FFmpeg -------------------------------------

def test_parse_devuelve_las_duraciones_en_orden():
    assert parse_internal_playlist(INTERNAL) == [6.0, 6.006, 2.5]


def test_parse_ignora_lineas_mal_formadas():
    # FFmpeg escribe el archivo mientras corre: se puede leer a medio flush.
    partial = "#EXTINF:6.000000,\nseg-00000.m4s\n#EXTINF:\n#EXTINF:abc,\n"

    assert parse_internal_playlist(partial) == [6.0]


def test_parse_de_playlist_sin_segmentos():
    assert parse_internal_playlist("#EXTM3U\n#EXT-X-VERSION:7\n") == []


def test_is_complete_detecta_el_endlist():
    assert is_complete(INTERNAL)
    assert not is_complete(INTERNAL.replace("#EXT-X-ENDLIST\n", ""))


# --- Duracion objetivo ------------------------------------------------------

def test_target_duration_redondea_hacia_arriba():
    # Con -c:v copy los cortes caen en keyframes y un segmento puede pasarse.
    assert target_duration([6.0, 8.3, 6.0]) == 9


def test_target_duration_sin_segmentos_es_uno():
    assert target_duration([]) == 1


# --- Media playlist ---------------------------------------------------------

def test_media_playlist_vod_cierra_con_endlist():
    playlist = build_media_playlist([6.0, 2.5], complete=True)

    assert "#EXT-X-PLAYLIST-TYPE:VOD" in playlist
    assert lines_of(playlist)[-1] == "#EXT-X-ENDLIST"


def test_media_playlist_en_construccion_es_event_y_no_cierra():
    playlist = build_media_playlist([6.0, 2.5], complete=False)

    assert "#EXT-X-PLAYLIST-TYPE:EVENT" in playlist
    assert "#EXT-X-ENDLIST" not in playlist


def test_media_playlist_declara_el_init_segment():
    playlist = build_media_playlist([6.0], init_uri="init.mp4")

    assert '#EXT-X-MAP:URI="init.mp4"' in playlist


def test_media_playlist_numera_los_segmentos_desde_cero():
    playlist = build_media_playlist([6.0, 6.0, 6.0])

    assert "seg-00000.m4s" in playlist
    assert "seg-00002.m4s" in playlist
    assert "seg-00003.m4s" not in playlist


def test_media_playlist_usa_las_duraciones_reales():
    # Declarar 6.000 uniforme cuando los segmentos no lo son desfasa los
    # subtitulos, y el error se acumula.
    playlist = build_media_playlist([6.006, 5.994])

    assert "#EXTINF:6.006000," in playlist
    assert "#EXTINF:5.994000," in playlist
    assert "#EXT-X-TARGETDURATION:7" in playlist


def test_playlists_de_dos_audios_son_identicas():
    # I4: el timeline no puede cambiar al cambiar de idioma.
    durations = [6.0, 6.0, 3.2]

    assert build_media_playlist(durations) == build_media_playlist(durations)


def test_las_dos_formas_del_nombre_de_segmento_coinciden():
    # Una va a FFmpeg (-hls_segment_filename) y la otra a la playlist.
    assert SEGMENT_TEMPLATE % 42 == SEGMENT_PATTERN.format(42)


# --- Master playlist --------------------------------------------------------

def renditions() -> list[AudioRendition]:
    return [
        AudioRendition(uri="audio/0/playlist.m3u8", name="Espanol", language="spa"),
        AudioRendition(uri="audio/1/playlist.m3u8", name="English", language="eng"),
    ]


def test_master_declara_una_rendition_por_pista():
    master = build_master_playlist(
        video_uri="video/playlist.m3u8", renditions=renditions(), bandwidth=4_500_000,
    )

    assert master.count("#EXT-X-MEDIA:") == 2
    assert 'URI="audio/0/playlist.m3u8"' in master
    assert 'URI="audio/1/playlist.m3u8"' in master


def test_master_marca_exactamente_una_pista_por_defecto():
    master = build_master_playlist(
        video_uri="video/playlist.m3u8", renditions=renditions(), bandwidth=1,
    )

    assert master.count("DEFAULT=YES") == 1
    assert master.count("DEFAULT=NO") == 1


def test_master_respeta_la_pista_marcada_como_default():
    tracks = renditions()
    tracks[1] = AudioRendition(
        uri="audio/1/playlist.m3u8", name="English", language="eng", default=True,
    )

    master = build_master_playlist(
        video_uri="video/playlist.m3u8", renditions=tracks, bandwidth=1,
    )
    media_lines = [ln for ln in lines_of(master) if ln.startswith("#EXT-X-MEDIA:")]

    assert "DEFAULT=NO" in media_lines[0]
    assert "DEFAULT=YES" in media_lines[1]


def test_master_ata_el_video_al_grupo_de_audio():
    master = build_master_playlist(
        video_uri="video/playlist.m3u8",
        renditions=renditions(),
        bandwidth=4_500_000,
        video_codec="avc1.640029",
        resolution=(1920, 1080),
    )

    assert 'AUDIO="aud"' in master
    assert 'CODECS="avc1.640029,mp4a.40.2"' in master
    assert "RESOLUTION=1920x1080" in master
    assert lines_of(master)[-1] == "video/playlist.m3u8"


def test_master_sin_audio_no_declara_grupo():
    master = build_master_playlist(
        video_uri="video/playlist.m3u8", renditions=[], bandwidth=1,
    )

    assert "#EXT-X-MEDIA:" not in master
    assert "AUDIO=" not in master


def test_master_escapa_comillas_del_titulo():
    # Los nombres salen de la metadata del archivo: pueden traer cualquier cosa.
    tracks = [
        AudioRendition(
            uri="audio/0/playlist.m3u8", name='Comentario "del director"', language="spa",
        )
    ]

    master = build_master_playlist(
        video_uri="video/playlist.m3u8", renditions=tracks, bandwidth=1,
    )
    media_line = next(ln for ln in lines_of(master) if ln.startswith("#EXT-X-MEDIA:"))

    assert media_line.count('"') % 2 == 0
    assert "Comentario 'del director'" in media_line


# --- Strings de codec -------------------------------------------------------

def test_avc_codec_string_de_high_4_1():
    assert avc_codec_string("High", 41) == "avc1.640029"


def test_avc_codec_string_de_main_3_0():
    assert avc_codec_string("Main", 30) == "avc1.4d401e"


def test_avc_codec_string_omite_lo_que_no_reconoce():
    # Mejor no declarar CODECS que declararlo mal: hls.js lo deduce del init.
    assert avc_codec_string("Perfil Raro", 41) is None
    assert avc_codec_string("High", None) is None
    assert avc_codec_string(None, 41) is None
    assert avc_codec_string("High", 0) is None


def test_aac_codec_string_por_perfil():
    assert aac_codec_string("LC") == "mp4a.40.2"
    assert aac_codec_string("HE-AAC") == "mp4a.40.5"


def test_aac_codec_string_cae_en_lc():
    # El encoder aac de FFmpeg produce LC, y es el default seguro.
    assert aac_codec_string(None) == "mp4a.40.2"
    assert aac_codec_string("desconocido") == "mp4a.40.2"
