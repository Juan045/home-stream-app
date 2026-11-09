using StreamMedia.Application.Cache.Interfaces;
using StreamMedia.Application.Streaming;

namespace StreamMedia.Application.Cache;

// GET /api/v1/admin/cache
// Recorre el cache en cada pedido y no guarda nada: es una foto. Puede superar el limite,
// porque el GC solo corre al abrir un asset nuevo y los builds en curso siguen escribiendo.
public sealed class GetCacheUsageUseCase(ICacheSpace space, CapacityLimits limits)
{
    public async Task<CacheUsage> ExecuteAsync(CancellationToken ct = default)
        => new(await space.TotalBytesAsync(ct), limits.MaxCacheBytes);
}
