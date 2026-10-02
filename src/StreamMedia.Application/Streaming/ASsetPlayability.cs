using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Streaming;

// Antes era Asset.playable en Python y contaba archivos: una entidad de Domain no toca el disco.
public static class AssetPlayability
{
    const int MinSegments = 2;

    // Se puede empezar a mirar: el video termino, o ya hay segmentos para arrancar.
    // Se cuentan archivos en disco, no entradas de playlist: aparecen mucho antes (I2).
    public static bool IsPlayable(Asset asset, IAssetStore store)
        => asset.Video.State == ArtifactState.Ready
           || store.CountSegments(store.Paths(asset.Id).Video) >= MinSegments;
}