namespace StreamMedia.Application.Cache.Interfaces;

// Espacio que ocupa output/ y su LRU: marca el uso, mide y borra los menos usados.
public interface ICacheSpace
{
    // Marca el asset como usado ahora (orden del LRU).
    Task TouchAsync(string assetId, CancellationToken ct = default);

    Task<long> TotalBytesAsync(CancellationToken ct = default);

    // Borra los menos usados hasta bajar de maxBytes. Ni los de "keep" ni los pineados.
    Task<IReadOnlyList<string>> CollectAsync(
        long maxBytes, IReadOnlySet<string>? keep = null, CancellationToken ct = default);
}
