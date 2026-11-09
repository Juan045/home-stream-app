using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Streaming.Interfaces;

public interface IAssetStore
{
    bool Exists(string assetId);

    // null si no existe o quedo ilegible.
    Task<AssetManifest?> ReadManifestAsync(string assetId, CancellationToken ct = default);
    Task WriteManifestAsync(string assetId, AssetManifest manifest, CancellationToken ct = default);

    // Marca el asset como usado ahora (orden del LRU).
    Task TouchAsync(string assetId, CancellationToken ct = default);

    Task<long> TotalBytesAsync(CancellationToken ct = default);

    // Borra los menos usados hasta bajar de maxBytes. Ni los de "keep" ni los pineados.
    Task<IReadOnlyList<string>> CollectAsync(
        long maxBytes, IReadOnlySet<string>? keep = null, CancellationToken ct = default);
}