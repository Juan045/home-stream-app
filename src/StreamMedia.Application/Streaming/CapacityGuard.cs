using StreamMedia.Application.MediaExceptions;
using StreamMedia.Application.Streaming.Interfaces;

namespace StreamMedia.Application.Streaming;

/// <summary>Limites de admision. Los arma Program.cs desde la configuracion.</summary>
public sealed record CapacityLimits(long MaxCacheBytes, int MaxConcurrent);

/// <summary>Control de admision: rechaza aperturas nuevas si no hay lugar o no hay espacio.</summary>
public sealed class CapacityGuard(
    IAssetStore store, AssetBuilder builder, SessionManager sessions, CapacityLimits limits)
{
    /// <exception cref="CapacityException">too_many_jobs o storage_limit.</exception>
    public async Task EnsureAsync(string source, CancellationToken ct = default)
    {
        var assetId = store.AssetIdFor(source);

        // Un asset ya abierto o cacheado no genera trabajo nuevo: siempre pasa.
        if (builder.Get(assetId) is not null || store.Exists(assetId))
            return;

        if (builder.BuildingCount() >= limits.MaxConcurrent)
            throw new CapacityException("too_many_jobs",
                "Hay demasiados procesos activos. Reintentar en unos segundos.");

        // Puede haber un pequeño hueco en el que se sobreescriba uno de los assets,
        // pero no se hace el lock ya que para una app con pocos usuarios es muy poco probable.
        //Si hay problemas de concurrencia este puede ser el punto a mejorar.
        // ponytail: con el cache lleno el disco se recorre dos veces (Collect + TotalBytes).
        // Si molesta, CollectAsync puede devolver el total que ya calcula, o un motor de cache lo lleva en memoria.
        var keep = sessions.ReferencedAssetIds().Union(builder.BuildingAssetIds()).ToHashSet();
        foreach (var id in await store.CollectAsync(limits.MaxCacheBytes, keep, ct))
            builder.Forget(id); // el prototipo no lo hace aca (solo en cleanup_loop)

        if (await store.TotalBytesAsync(ct) >= limits.MaxCacheBytes)
            throw new CapacityException("storage_limit",
                "El cache esta lleno y no hay nada que liberar. Cerrar sesiones activas.");
    }
}