using StreamMedia.Application.Cache.Interfaces;
using StreamMedia.Application.Streaming;
using StreamMedia.Domain.DTO;

namespace StreamMedia.Infrastructure.Cache;

public sealed class HlsOutput : IHlsOutput
{
    public void CreateDirectories(AssetPaths paths)
    {
        Directory.CreateDirectory(paths.Video);
        Directory.CreateDirectory(paths.Subs);
        Directory.CreateDirectory(paths.AudioRoot);
    }

    public void PrepareBuild(string directory)
    {
        Directory.CreateDirectory(directory);                              // no falla si ya existe
        File.Delete(Path.Combine(directory, HlsLayout.InternalPlaylist)); // tampoco falla si no existe

        // Un build relanzado puede usar otro codec: sin esto quedarian segmentos mezclados y
        // CountSegments contaria los viejos. Solo los segmentos: paths.Subs tambien pasa por aca
        // y se perderian los .vtt.
        foreach (var segment in Directory.GetFiles(directory, HlsLayout.SegmentGlob))
            File.Delete(segment);
    }

    public int CountSegments(string directory)
        => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, HlsLayout.SegmentGlob).Count()
            : 0;

    public async Task<string?> ReadInternalPlaylistAsync(string directory, CancellationToken ct = default)
    {
        try { return await File.ReadAllTextAsync(Path.Combine(directory, HlsLayout.InternalPlaylist), ct); }
        // FileNotFound y DirectoryNotFound heredan de IOException. En Windows, si FFmpeg lo tiene
        // abierto en ese instante, el rename o la lectura fallan: el cliente reintenta.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public bool IsPlaylistComplete(string directory)
    {
        try
        {
            return PlaylistBuilder.IsComplete(File.ReadAllText(Path.Combine(directory, HlsLayout.InternalPlaylist)));
        } catch (IOException) { return false; } // Todavia no existe, o FFMPEG la esta escribiendo
    }

    public bool FileExists(string path) => File.Exists(path);
}
