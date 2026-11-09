using StreamMedia.Domain.DTO;

namespace StreamMedia.Application.Cache.Interfaces;

// Lo que FFmpeg deja en output/: lo prepara antes del build y lo lee del disco. El formato lo interpreta PlaylistBuilder.
public interface IHlsOutput
{
    // Crea el arbol de directorios del asset (video/, subs/, audio/). No lo marca como usado.
    void CreateDirectories(AssetPaths paths);

    // crea la carpeta y borra el internal.m3u8 y los segmentos de un build previo a medias
    void PrepareBuild(string directory);

    // Cuantos segmentos .m4s hay en disco en esa carpeta (0 si no existe).
    // TODO: cambiar a "hay al menos N segmentos?" (por ejemplo HasSegments(directory, min)):
    // AssetPlayability.IsPlayable solo compara contra 2 y GET /sessions/{id} lo llama cada 2 s por espectador.
    int CountSegments(string directory);

    // null si todavia no existe: FFmpeg lo crea con el primer segmento y lo reescribe con un rename.
    Task<string?> ReadInternalPlaylistAsync(string directory, CancellationToken ct = default);

    bool IsPlaylistComplete(string directory);
    bool FileExists(string path);
}
