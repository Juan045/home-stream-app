using StreamMedia.Application.Cache;
using StreamMedia.Application.Cache.Interfaces;
using StreamMedia.Application.Streaming;
using StreamMedia.Domain.DTO;

namespace StreamMedia.Infrastructure.Cache;

public sealed class HlsOutput(CacheLayout layout) : IHlsOutput
{
    public void CreateDirectories(string assetId)
    {
        var paths = layout.Paths(assetId);
        Directory.CreateDirectory(paths.Video);
        Directory.CreateDirectory(paths.Subs);
        Directory.CreateDirectory(paths.AudioRoot);
    }

    public void PrepareBuild(string assetId, OutputFolder folder)
    {
        var directory = layout.Dir(assetId, folder);
        Directory.CreateDirectory(directory);                              // no falla si ya existe
        File.Delete(Path.Combine(directory, HlsLayout.InternalPlaylist)); // tampoco falla si no existe

        // Un build relanzado puede usar otro codec: sin esto quedarian segmentos mezclados y
        // CountSegments contaria los viejos. Solo los segmentos: la carpeta de subs tambien pasa por aca
        // y se perderian los .vtt.
        foreach (var segment in Directory.GetFiles(directory, HlsLayout.SegmentGlob))
            File.Delete(segment);
    }

    public int CountSegments(string assetId, OutputFolder folder)
    {
        var directory = layout.Dir(assetId, folder);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, HlsLayout.SegmentGlob).Count()
            : 0;
    }

    public async Task<string?> ReadInternalPlaylistAsync(string assetId, OutputFolder folder, CancellationToken ct = default)
    {
        var directory = layout.Dir(assetId, folder);
        try { return await File.ReadAllTextAsync(Path.Combine(directory, HlsLayout.InternalPlaylist), ct); }
        // FileNotFound y DirectoryNotFound heredan de IOException. En Windows, si FFmpeg lo tiene
        // abierto en ese instante, el rename o la lectura fallan: el cliente reintenta.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public bool IsPlaylistComplete(string assetId, OutputFolder folder)
    {
        var directory = layout.Dir(assetId, folder);
        try
        {
            return PlaylistBuilder.IsComplete(File.ReadAllText(Path.Combine(directory, HlsLayout.InternalPlaylist)));
        } catch (IOException) { return false; } // Todavia no existe, o FFMPEG la esta escribiendo
    }

    public string? FindFile(string assetId, OutputFolder folder, string fileName)
    {
        // Defensa propia: con separadores o ".." el archivo quedaria fuera de la carpeta.
        if (fileName.Length == 0 || Path.GetFileName(fileName) != fileName || fileName is "." or "..")
            return null;

        // Absoluta: PhysicalFile la exige y Cache:Dir puede ser relativo.
        var fullPath = Path.GetFullPath(Path.Combine(layout.Dir(assetId, folder), fileName));
        return File.Exists(fullPath) ? fullPath : null;
    }
}
