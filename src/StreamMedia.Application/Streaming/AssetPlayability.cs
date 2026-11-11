using StreamMedia.Domain.Entities;
using StreamMedia.Application.Cache;
using StreamMedia.Application.Cache.Interfaces;

namespace StreamMedia.Application.Streaming;

// Antes era Asset.playable en Python y contaba archivos: una entidad de Domain no toca el disco.
public static class AssetPlayability
{
    const int MinSegments = 2;

    // Se puede empezar a mirar: el video termino, o ya hay segmentos para arrancar. Un video failed
    // no, aunque tenga segmentos: pueden estar cortados o mal formados, y por eso ni se cuentan.
    // Se cuentan archivos en disco, no entradas de playlist: aparecen mucho antes (I2).
    public static bool IsPlayable(Asset asset, IHlsOutput hls) => asset.Video.State switch
    {
        ArtifactState.Ready => true,
        ArtifactState.Failed => false,
        _ => hls.CountSegments(asset.Id, OutputFolder.Video) >= MinSegments,
    };
}