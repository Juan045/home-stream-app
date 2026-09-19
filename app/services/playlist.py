"""Construccion de las playlists HLS que ve el cliente.

Logica pura: no toca disco ni invoca FFmpeg. Las playlists se *calculan* a
partir de las duraciones reales que FFmpeg reporto en su `internal.m3u8`; ese
archivo nunca se sirve al cliente.

Las tres invariantes que sostienen la sincronizacion viven aca:

- La playlist declara todos los segmentos generados, en orden, desde el cero.
- Las duraciones `#EXTINF` son las reales, no una estimacion: se leen de la
  salida de FFmpeg. Un `#EXTINF` que miente desfasa los subtitulos, y el error
  se acumula con el tiempo de reproduccion.
- Las media playlists de dos pistas de audio distintas son identicas salvo la
  URI base. Es lo que garantiza que el timeline no cambie al cambiar de idioma.
"""

from __future__ import annotations

from dataclasses import dataclass
from math import ceil

# Nombres compartidos con `transcoder`: FFmpeg recibe la forma printf y la
# playlist se arma con la forma `str.format`. Tienen que coincidir.
INIT_NAME = "init.mp4"
SEGMENT_TEMPLATE = "seg-%05d.m4s"   # para -hls_segment_filename
SEGMENT_PATTERN = "seg-{:05d}.m4s"  # para armar la playlist
INTERNAL_PLAYLIST = "internal.m3u8"

AUDIO_GROUP = "aud"
HLS_VERSION = 7  # fMP4 requiere >= 6; 7 por #EXT-X-INDEPENDENT-SEGMENTS

EXTINF_PREFIX = "#EXTINF:"
ENDLIST_TAG = "#EXT-X-ENDLIST"

# ffprobe devuelve el nombre del perfil; HLS quiere el par (profile_idc,
# constraint_flags) en hexadecimal.
AVC_PROFILES: dict[str, tuple[int, int]] = {
    "constrained baseline": (0x42, 0xE0),
    "baseline": (0x42, 0xE0),
    "main": (0x4D, 0x40),
    "extended": (0x58, 0x00),
    "high": (0x64, 0x00),
    "high 10": (0x6E, 0x00),
    "high 4:2:2": (0x7A, 0x00),
    "high 4:4:4 predictive": (0xF4, 0x00),
}

AAC_PROFILES: dict[str, int] = {
    "main": 1,
    "lc": 2,
    "ssr": 3,
    "ltp": 4,
    "he-aac": 5,
    "he-aacv2": 29,
}

AAC_LC = "mp4a.40.2"


@dataclass(frozen=True)
class AudioRendition:
    """Una pista de audio declarada como `#EXT-X-MEDIA` en el master."""

    uri: str
    name: str
    language: str
    channels: int = 2
    codec: str = AAC_LC
    default: bool = False


def parse_internal_playlist(text: str) -> list[float]:
    """Extrae las duraciones reales de los `#EXTINF` de una playlist.

    Se usa sobre el `internal.m3u8` que escribe FFmpeg, que es la unica fuente
    de verdad de cuanto dura cada segmento. Ignora lineas mal formadas en vez
    de fallar: FFmpeg escribe el archivo mientras corre y puede leerse a mitad
    de un flush.
    """
    durations: list[float] = []
    for raw in text.splitlines():
        line = raw.strip()
        if not line.startswith(EXTINF_PREFIX):
            continue
        value = line[len(EXTINF_PREFIX):].split(",", 1)[0].strip()
        try:
            durations.append(float(value))
        except ValueError:
            continue
    return durations


def is_complete(text: str) -> bool:
    """True si FFmpeg ya cerro la playlist con `#EXT-X-ENDLIST`."""
    return ENDLIST_TAG in text


def target_duration(durations: list[float]) -> int:
    """Entero >= al segmento mas largo, como pide la especificacion de HLS."""
    if not durations:
        return 1
    return max(1, ceil(max(durations)))


def build_media_playlist(
    durations: list[float],
    *,
    init_uri: str = INIT_NAME,
    segment_pattern: str = SEGMENT_PATTERN,
    complete: bool = True,
) -> str:
    """Arma la media playlist de una pista (video o audio).

    `complete=False` la emite como EVENT y sin `#EXT-X-ENDLIST`: el build
    todavia esta corriendo y la lista va a crecer. Cuando el build termina se
    emite como VOD, y recien ahi el player puede buscar en todo el archivo.
    """
    lines = [
        "#EXTM3U",
        f"#EXT-X-VERSION:{HLS_VERSION}",
        f"#EXT-X-TARGETDURATION:{target_duration(durations)}",
        "#EXT-X-MEDIA-SEQUENCE:0",
        f"#EXT-X-PLAYLIST-TYPE:{'VOD' if complete else 'EVENT'}",
        f'#EXT-X-MAP:URI="{init_uri}"',
    ]

    for index, seconds in enumerate(durations):
        lines.append(f"#EXTINF:{seconds:.6f},")
        lines.append(segment_pattern.format(index))

    if complete:
        lines.append(ENDLIST_TAG)

    return "\n".join(lines) + "\n"


def build_master_playlist(
    *,
    video_uri: str,
    renditions: list[AudioRendition],
    bandwidth: int,
    video_codec: str | None = None,
    resolution: tuple[int, int] | None = None,
    group_id: str = AUDIO_GROUP,
) -> str:
    """Arma el master con el video y las pistas de audio como renditions.

    Declarar el audio con `#EXT-X-MEDIA` es lo que permite que cambiar de
    idioma sea `hls.audioTrack = n` en el cliente: no se recarga el manifest,
    no se pierde la posicion y no se reinicia FFmpeg.

    Exactamente una rendition queda marcada `DEFAULT=YES`.
    """
    lines = [
        "#EXTM3U",
        f"#EXT-X-VERSION:{HLS_VERSION}",
        "#EXT-X-INDEPENDENT-SEGMENTS",
    ]

    default_index = next(
        (i for i, r in enumerate(renditions) if r.default),
        0 if renditions else -1,
    )

    for index, rendition in enumerate(renditions):
        attributes = [
            "TYPE=AUDIO",
            f'GROUP-ID="{group_id}"',
            f'NAME="{_quote(rendition.name)}"',
            f'LANGUAGE="{_quote(rendition.language)}"',
            f"DEFAULT={'YES' if index == default_index else 'NO'}",
            "AUTOSELECT=YES",
            f'CHANNELS="{rendition.channels}"',
            f'URI="{rendition.uri}"',
        ]
        lines.append("#EXT-X-MEDIA:" + ",".join(attributes))

    stream = [f"BANDWIDTH={int(bandwidth)}"]
    if resolution is not None:
        stream.append(f"RESOLUTION={resolution[0]}x{resolution[1]}")

    # O se declaran los dos codecs, o ninguno. Declarar solo el del audio no es
    # "omitir el del video": anuncia un variant **sin** video. hls.js lo lee
    # como `{audio: true, video: false}` y entonces espera un solo
    # `BUFFER_CODECS` cuando van a llegar dos —el del video y el de la
    # rendition—, crea los SourceBuffers con el primero y el segundo se queda
    # sin buffer. Omitir el atributo entero es valido, porque es opcional, y
    # ahi si el player deduce los dos del init segment.
    #
    # Pasa cada vez que el codec de video no se puede escribir: un AV1, que no
    # declara nivel, o un H.264 con un perfil que `avc_codec_string` no conoce.
    if video_codec:
        audio_codec = _default_audio_codec(renditions, default_index)
        declared = [video_codec, audio_codec] if audio_codec else [video_codec]
        stream.append(f'CODECS="{",".join(declared)}"')
    if renditions:
        stream.append(f'AUDIO="{group_id}"')

    lines.append("#EXT-X-STREAM-INF:" + ",".join(stream))
    lines.append(video_uri)

    return "\n".join(lines) + "\n"


def avc_codec_string(profile: str | None, level: int | None) -> str | None:
    """`avc1.640029` a partir del perfil y nivel que reporta ffprobe.

    Devuelve None si los datos no alcanzan: el atributo CODECS es opcional y
    hls.js deduce el codec del init segment, asi que es preferible omitirlo
    antes que declararlo mal.
    """
    if not profile or level is None:
        return None

    codes = AVC_PROFILES.get(profile.strip().lower())
    if codes is None:
        return None

    try:
        level_value = int(level)
    except (TypeError, ValueError):
        return None

    if not 0 < level_value <= 0xFF:
        return None

    profile_idc, constraints = codes
    return f"avc1.{profile_idc:02x}{constraints:02x}{level_value:02x}"


def aac_codec_string(profile: str | None = None) -> str:
    """`mp4a.40.N` segun el perfil AAC. LC por defecto.

    LC es lo correcto tanto cuando re-codificamos (el encoder aac de FFmpeg
    produce LC) como cuando el perfil del origen es desconocido.
    """
    if not profile:
        return AAC_LC
    return f"mp4a.40.{AAC_PROFILES.get(profile.strip().lower(), 2)}"


def _default_audio_codec(
    renditions: list[AudioRendition], default_index: int
) -> str | None:
    if not renditions or default_index < 0:
        return None
    return renditions[default_index].codec


def _quote(value: str) -> str:
    """Deja el texto usable dentro de una quoted-string de HLS.

    Los titulos vienen de la metadata del archivo, asi que pueden traer
    cualquier cosa. Las comas si estan permitidas dentro de comillas.
    """
    cleaned = value.replace('"', "'")
    return "".join(ch for ch in cleaned if ch >= " " and ch != "\x7f")
