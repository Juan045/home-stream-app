using System.Globalization;
using Microsoft.Extensions.Logging;
using StreamMedia.Application.Cache;
using StreamMedia.Application.Cache.Interfaces;

namespace StreamMedia.Infrastructure.Cache;

// El pin sale de ReadManifestAsync: la lectura barata, sin recorrer el disco.
public sealed class CacheSpace(CacheLayout layout, IAssetRepository manifests, TimeProvider time, ILogger<CacheSpace> log)
    : ICacheSpace
{
    // --- Uso reciente y espacio -------------------------------------------------

    public async Task TouchAsync(string assetId, CancellationToken ct = default)
    {
        var paths = layout.Paths(assetId);
        if (!Directory.Exists(paths.Root)) return; // no resucitar un asset ya borrado
        await File.WriteAllTextAsync(paths.AccessMarker, Now(), ct);
    }

    // 0.0 si nunca se marco (el mas viejo del LRU).
    double LastAccess(string assetId)
    {
        try
        {
            var text = File.ReadAllText(layout.Paths(assetId).AccessMarker);
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                ? seconds
                : 0.0;
        }
        catch (IOException) { return 0.0; }
    }

    // ponytail: el ct solo evita arrancar; un recorrido empezado llega hasta el final aunque se
    // cancele la request. Si GET /admin/cache tarda con el cache lleno en un bind mount, pasar el
    // ct a TreeSize y chequearlo por archivo.
    public Task<long> TotalBytesAsync(CancellationToken ct = default)
        => Task.Run(() => TreeSize(layout.Root), ct);

    // --- Limpieza -----------------------------------------------------------------

    bool Remove(string assetId)
    {
        try
        {
            Directory.Delete(layout.Paths(assetId).Root, recursive: true);
        }
        catch (DirectoryNotFoundException) { return true; } // ya no estaba: da igual
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // En Windows un segmento abierto por FFmpeg no se puede borrar.
            log.LogWarning(ex, "no se pudo borrar el asset {AssetId}", assetId);
            return false;
        }

        log.LogInformation("asset borrado {AssetId}", assetId);
        return true;
    }

    public async Task<IReadOnlyList<string>> CollectAsync(
        long maxBytes, IReadOnlySet<string>? keep = null, CancellationToken ct = default)
    {
        var total = await TotalBytesAsync(ct);
        if (total <= maxBytes) return [];

        // El pin se chequea aca adentro y no en "keep": son varios los que llaman a este
        // metodo y olvidarselo en uno solo alcanza para perder horas de codificacion.
        var candidates = new List<(string Id, double Access)>();
        foreach (var id in AssetIds())
        {
            if (keep is not null && keep.Contains(id)) continue;
            if (await manifests.ReadManifestAsync(id, ct) is { Pinned: true }) continue;
            candidates.Add((id, LastAccess(id)));
        }

        var removed = new List<string>();
        foreach (var (id, _) in candidates.OrderBy(c => c.Access)) // OrderBy es estable
        {
            if (total <= maxBytes) break;

            var size = await Task.Run(() => TreeSize(layout.Paths(id).Root), ct);
            if (!await Task.Run(() => Remove(id), ct)) continue;

            total -= size;
            removed.Add(id);
        }

        if (removed.Count > 0)
            log.LogInformation("gc completado: {Removed} assets, {TotalBytes} bytes", removed.Count, total);

        return removed;
    }

    // --- Internos -----------------------------------------------------------------

    IEnumerable<string> AssetIds()
        => Directory.EnumerateDirectories(layout.Root).Select(d => Path.GetFileName(d)).Order();

    // Segundos Unix con decimales, igual que el marcador del prototipo.
    string Now()
        => (time.GetUtcNow().ToUnixTimeMilliseconds() / 1000.0).ToString("F3", CultureInfo.InvariantCulture);

    static long TreeSize(string root)
    {
        if (!Directory.Exists(root)) return 0;

        long total = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", options))
        {
            try { total += file.Length; }
            catch (IOException) { } // desaparecio entre el listado y la lectura
        }
        return total;
    }
}
