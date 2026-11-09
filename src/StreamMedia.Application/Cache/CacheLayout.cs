using StreamMedia.Domain.DTO;

namespace StreamMedia.Application.Cache;

// Unico lugar que arma las rutas del cache. Calculo puro: no toca el disco.
public sealed record CacheLayout(string Root)
{
    public AssetPaths Paths(string assetId) => new(Path.Combine(Root, assetId));
}
