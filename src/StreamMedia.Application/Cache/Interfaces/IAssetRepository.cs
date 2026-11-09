using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Cache.Interfaces;

// El manifest.json de cada asset: la unica fuente de verdad que cruza reinicios.
public interface IAssetRepository
{
    bool Exists(string assetId);

    // null si no existe o quedo ilegible.
    Task<AssetManifest?> ReadManifestAsync(string assetId, CancellationToken ct = default);
    Task WriteManifestAsync(string assetId, AssetManifest manifest, CancellationToken ct = default);
}