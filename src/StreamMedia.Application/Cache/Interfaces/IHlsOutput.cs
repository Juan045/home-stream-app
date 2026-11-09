namespace StreamMedia.Application.Cache.Interfaces;

// Lo que FFmpeg deja en output/: lo prepara antes del build y lo lee del disco. El formato lo interpreta PlaylistBuilder.
// Se pide por asset y carpeta, no por ruta: los llamadores no arman rutas del cache.
public interface IHlsOutput
{
    // Crea el arbol de directorios del asset (video/, subs/, audio/). No lo marca como usado.
    void CreateDirectories(string assetId);

    // crea la carpeta y borra el internal.m3u8 y los segmentos de un build previo a medias
    void PrepareBuild(string assetId, OutputFolder folder);

    // Cuantos segmentos .m4s hay en disco en esa carpeta (0 si no existe).
    // TODO: cambiar a "hay al menos N segmentos?" (por ejemplo HasSegments(assetId, folder, min)):
    // AssetPlayability.IsPlayable solo compara contra 2 y GET /sessions/{id} lo llama cada 2 s por espectador.
    int CountSegments(string assetId, OutputFolder folder);

    // null si todavia no existe: FFmpeg lo crea con el primer segmento y lo reescribe con un rename.
    Task<string?> ReadInternalPlaylistAsync(string assetId, OutputFolder folder, CancellationToken ct = default);

    bool IsPlaylistComplete(string assetId, OutputFolder folder);

    // Ruta absoluta del archivo, o null si no existe o el nombre no es simple.
    string? FindFile(string assetId, OutputFolder folder, string fileName);
}
