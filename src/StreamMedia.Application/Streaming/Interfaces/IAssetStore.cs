using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Streaming.Interfaces;

public interface IAssetStore
{
    bool Exists(string assetId);

    // null si no existe o quedo ilegible.
    Task<AssetManifest?> ReadManifestAsync(string assetId, CancellationToken ct = default);
    Task WriteManifestAsync(string assetId, AssetManifest manifest, CancellationToken ct = default);
}