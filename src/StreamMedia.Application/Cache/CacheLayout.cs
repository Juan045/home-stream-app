using StreamMedia.Domain.DTO;

namespace StreamMedia.Application.Cache;

// Unico lugar que arma las rutas del cache. Calculo puro: no toca el disco.
public sealed record CacheLayout(string Root)
{
    public AssetPaths Paths(string assetId) => new(Path.Combine(Root, assetId));

    // Unico lugar que traduce una carpeta de salida a su ruta.
    public string Dir(string assetId, OutputFolder folder) => folder.Kind switch
    {
        OutputFolderKind.Video => Paths(assetId).Video,
        OutputFolderKind.Audio => Paths(assetId).Audio(folder.Track),
        OutputFolderKind.Subs => Paths(assetId).Subs,
        _ => throw new ArgumentOutOfRangeException(nameof(folder)),
    };
}
