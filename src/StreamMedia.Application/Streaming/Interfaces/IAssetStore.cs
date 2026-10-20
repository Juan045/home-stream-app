using StreamMedia.Domain.DTO;

namespace StreamMedia.Application.Streaming.Interfaces;

public interface IAssetStore
{
    // Identidad (ruta, mtime, tamano). Lanza FileNotFoundException si el origen no existe.
    string AssetIdFor(string source);

    AssetPaths Paths(string assetId);

    // Crea el arbol de directorios del asset y lo marca como usado.
    AssetPaths Prepare(string assetId);
    string PrepareAudio(string assetId, int track);

    bool Exists(string assetId);

    // Cuantos segmentos .m4s hay en disco en esa carpeta (0 si no existe).
    // TODO: cambiar a "hay al menos N segmentos?" (por ejemplo HasSegments(directory, min)):
    // IsPlayable solo compara contra 2 y GET /sessions/{id} lo llama cada 2 s por espectador.
    int CountSegments(string directory);

    // null si no existe o quedo ilegible.
    Task<AssetManifest?> ReadManifestAsync(string assetId, CancellationToken ct = default);
    Task WriteManifestAsync(string assetId, AssetManifest manifest, CancellationToken ct = default);

    // Marca el asset como usado ahora (orden del LRU).
    Task TouchAsync(string assetId, CancellationToken ct = default);

    Task<long> TotalBytesAsync(CancellationToken ct = default);

    // Borra los menos usados hasta bajar de maxBytes. Ni los de "keep" ni los pineados.
    Task<IReadOnlyList<string>> CollectAsync(
        long maxBytes, IReadOnlySet<string>? keep = null, CancellationToken ct = default);

    // null si todavia no existe: FFmpeg lo crea con el primer segmento y lo reescribe con un rename.
    Task<string?> ReadInternalPlaylistAsync(string directory, CancellationToken ct = default);

    bool IsPlaylistComplete(string directory);
    bool FileExists(string path);

    // crea la carpeta y borra el internal.m3u8 de un build previo a medias
    void PrepareBuild(string directory);
}