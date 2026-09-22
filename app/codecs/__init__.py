"""Encoders de video de salida, indexados por nombre.

Un encoder es un nombre corto (`h264`, `av1`) que mapea a dos cosas: los
argumentos de FFmpeg que lo configuran, y el string de CODECS que el master
declara cuando el video sale codificado con el.

`transcoder.build_video_args` lo busca aca por `TranscodeOptions.video_codec`.
Agregar uno nuevo es agregar una entrada al diccionario `VIDEO` y nada mas: ni
el transcoder ni el builder se tocan.

Lo que este modulo **no** decide es *si* hay que codificar. Eso sale del codec
del origen (`media_analyzer.StreamStrategy`): si el archivo ya viene en un
codec que el navegador reproduce, se copia con `-c:v copy` y aca no se entra.
Este modulo describe el destino, no el origen.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import TYPE_CHECKING, Callable, Sequence

if TYPE_CHECKING:  # solo para el type hint: importarlo en runtime es un ciclo
    from app.services.transcoder import TranscodeOptions

ArgsBuilder = Callable[["TranscodeOptions", Sequence[str]], list[str]]


@dataclass(frozen=True)
class VideoEncoder:
    """Como codificar con un encoder y como declararlo en el master."""

    encoder: str
    build_args: ArgsBuilder
    codec_string: str | None
    """CODECS a declarar cuando el video se codifica con este encoder.

    `None` significa no declararlo: el atributo es opcional y el player lo
    deduce del init segment, que es preferible a declararlo mal.
    """


def _video_filters(
    options: "TranscodeOptions", source_filters: Sequence[str],
) -> list[str]:
    """Une filtros que dependen del origen con el scale del perfil.

    El encoder conserva sus filtros de salida (escala); quien conoce el
    archivo de entrada aporta, por ejemplo, el tone-mapping HDR. FFmpeg acepta
    una sola cadena ``-vf``: varios ``-vf`` se pisan entre si.
    """
    filters = [*source_filters]
    if options.video_max_height is not None:
        filters.append(f"scale=-2:'min({options.video_max_height},ih)'")
    return ["-vf", ",".join(filters)] if filters else []


def _key_frames(options: "TranscodeOptions") -> list[str]:
    """Un keyframe exacto en cada borde de segmento.

    Sin esto los cortes caen donde el encoder haya puesto un keyframe y las
    duraciones dejan de ser multiplos del segmento configurado.
    """
    return ["-force_key_frames", f"expr:gte(t,n_forced*{options.hls_time})"]


def _libx264_args(
    options: "TranscodeOptions", source_filters: Sequence[str],
) -> list[str]:
    """H.264. El unico codec que reproduce cualquier navegador.

    `-sc_threshold 0` desactiva los keyframes por cambio de escena, que
    correrian los cortes que fija `-force_key_frames`.
    """
    return [
        "-c:v", "libx264",
        "-preset", options.preset,
        "-crf", str(options.crf),
        "-maxrate", options.video_max_bitrate,
        "-bufsize", options.video_bufsize,
        "-pix_fmt", "yuv420p",
        "-profile:v", "high",
        "-level", "4.1",
        *_video_filters(options, source_filters),
        *_key_frames(options),
        "-sc_threshold", "0",
    ]


# El `-preset` de SVT-AV1 es un numero (0 = mas lento y mejor, 13 = mas rapido)
# y no un nombre. Se traduce el del preset de x264 para que `SM_FFMPEG_PRESET`
# signifique lo mismo con los dos encoders.
SVTAV1_PRESETS: dict[str, int] = {
    "ultrafast": 12,
    "superfast": 11,
    "veryfast": 10,
    "faster": 9,
    "fast": 8,
    "medium": 7,
    "slow": 5,
    "slower": 3,
    "veryslow": 1,
}
SVTAV1_DEFAULT_PRESET = 8


def svtav1_preset(preset: str) -> str:
    """El `-preset` de SVT-AV1 a partir del de la configuracion.

    Acepta las dos formas porque las dos aparecen: `SM_FFMPEG_PRESET` viene con
    los nombres de x264, y el perfil de biblioteca fija el numero que pide la
    especificacion (6), que no tiene nombre equivalente. Un numero pasa tal
    cual; un nombre desconocido cae en el default.
    """
    value = preset.strip()
    if value.isdigit():
        return value
    return str(SVTAV1_PRESETS.get(value.lower(), SVTAV1_DEFAULT_PRESET))


def _libsvtav1_args(
    options: "TranscodeOptions", source_filters: Sequence[str],
) -> list[str]:
    """AV1 por SVT-AV1, el unico encoder de AV1 con velocidad usable.

    Dos advertencias antes de usarlo:

    - **El CRF no es la misma escala que en x264.** 23 en AV1 es bastante mas
      calidad (y mas tamano) que 23 en x264; el rango util esta entre 30 y 40.
      Hay que subir `SM_FFMPEG_CRF` al cambiar de encoder.
    - **Sigue siendo mucho mas lento que x264**, incluso en preset 10+. Para el
      caso de "abrir una pelicula y mirarla ahora" H.264 gana siempre; AV1 es
      para reprocesar algo que se va a ver muchas veces — o sea, para `ARCHIVE`.

    No lleva `-maxrate`/`-bufsize` ni `-profile`/`-level` a proposito: el CRF
    con tope depende de la version de SVT-AV1 compilada, y sin `-level` fijo no
    se puede declarar el CODECS sin mentir en el nivel (por eso el encoder lo
    declara como `None`).
    """
    return [
        "-c:v", "libsvtav1",
        "-preset", svtav1_preset(options.preset),
        "-crf", str(options.crf),
        "-pix_fmt", "yuv420p",
        *_video_filters(options, source_filters),
        # SVT-AV1 respeta los keyframes forzados desde FFmpeg 6; en builds mas
        # viejos los ignora y los segmentos salen de duracion despareja. No
        # rompe la reproduccion (las duraciones de la playlist son las reales
        # que reporta FFmpeg) pero el seek queda mas grueso.
        *_key_frames(options),
    ]


VIDEO: dict[str, VideoEncoder] = {
    "h264": VideoEncoder(
        encoder="libx264",
        build_args=_libx264_args,
        # Los parametros de arriba son fijos (high@4.1), asi que el string de
        # codec del video codificado se conoce de antemano.
        codec_string="avc1.640029",
    ),
    "av1": VideoEncoder(
        encoder="libsvtav1",
        build_args=_libsvtav1_args,
        codec_string=None,
    ),
}

DEFAULT_VIDEO = "h264"


# Perfil de la codificacion asincrona de biblioteca, segun ESPECIFICACION.md
# (version cerrada del 18 sep 2026, medida sobre 4K con SVT-AV1 4.2 y ffmpeg 8).
#
# Va como diccionario de overrides y no como un `TranscodeOptions` armado porque
# ese dataclass vive en `transcoder`, que importa este modulo: construirlo aca
# seria un ciclo. Quien lo usa hace `dataclasses.replace(options, **ARCHIVE)`,
# asi hereda de la configuracion del server lo que el perfil no fija (duracion
# del segmento, audio).
#
# Lo que la especificacion pide y aca **no** esta, porque no sobrevive al
# empaquetado HLS de este proyecto:
#
# - El contenedor. Habla de WebM y de `-movflags +faststart` para MP4; el
#   pipeline segmenta en fMP4/CMAF y no produce ningun archivo progresivo.
# - El audio en Opus. Opus en fMP4 no esta en la especificacion de HLS y Safari
#   no lo toma. El objetivo real de ese parrafo —no dejar pasar DTS-HD ni AC3—
#   ya lo cumple el pipeline transcodificando a AAC, y `force_transcode` hace
#   que aca se aplique siempre.
# - El `-g 120`. El keyframe en el borde de cada segmento lo fija
#   `-force_key_frames`, que es mas estricto y es de lo que dependen las
#   duraciones de la playlist.
ARCHIVE: dict[str, object] = {
    "video_codec": "av1",
    "crf": 32,
    "preset": "8",
    # Resolucion del origen, sin tope: una copia que se guarda para siempre no
    # se recorta. TODO: hacerlo configurable por perfil si algun dia hace falta
    # una biblioteca a 1080p.
    "video_max_height": 1080,
    # Sin esto una pelicula que ya es H.264 pasa por `video_copy_allowed` y se
    # copiaria en vez de codificarse en AV1.
    "force_transcode": True,
}


def video(name: str) -> VideoEncoder:
    """Busca el encoder por nombre. Falla nombrando los que hay."""
    try:
        return VIDEO[name]
    except KeyError:
        raise ValueError(
            f"Codec de video desconocido: {name!r} "
            f"(disponibles: {', '.join(sorted(VIDEO))})"
        ) from None
