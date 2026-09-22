"""Clasificacion y conversion de color para el video de salida.

La deteccion vive cerca de ``media_analyzer`` y los filtros son datos puros:
este modulo no conoce FFmpeg como proceso, assets ni el encoder que los usa.
"""

from __future__ import annotations

from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from app.services.media_analyzer import SourceInfo


# ffprobe usa estos nombres para las dos transferencias HDR que soportamos.
HDR_TRANSFERS = frozenset({"smpte2084", "arib-std-b67"})

# Se declaran tambien como metadata del stream de salida. Aunque zscale
# propaga esos valores, fijarlos evita que el contenedor conserve tags HDR del
# input cuando se generen los segmentos fMP4.
SDR_BT709_OUTPUT_ARGS = [
    "-color_primaries", "bt709",
    "-color_trc", "bt709",
    "-colorspace", "bt709",
    "-color_range", "tv",
]


def is_hdr_transfer(transfer: str | None) -> bool:
    """True para las curvas HDR que FFmpeg puede pasar a SDR."""
    return transfer in HDR_TRANSFERS


def hdr_to_sdr_filters(info: "SourceInfo") -> list[str]:
    """Devuelve el filtro HDR -> SDR BT.709, o nada para una fuente SDR.

    ``tonemap`` trabaja sobre luz lineal y valores float; los dos ``zscale``
    hacen las conversiones de transferencia, gamut y matriz. FFmpeg toma los
    parametros de entrada desde los tags que detecto ffprobe.
    """
    if not is_hdr_transfer(info.video_color_transfer):
        return []

    return [
        "zscale=transfer=linear:npl=100",
        "format=gbrpf32le",
        "tonemap=tonemap=mobius:desat=0",
        "zscale=primaries=bt709:transfer=bt709:matrix=bt709:range=tv",
        "format=yuv420p",
    ]
