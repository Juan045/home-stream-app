using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StreamMedia.Application.Cache;
using StreamMedia.Application.Streaming.Interfaces;
using StreamMedia.Domain.Entities;

namespace StreamMedia.Infrastructure.Cache;

// La raiz ya llega absoluta y creada: eso lo hace el registro de CacheLayout en Program.cs.
public sealed class AssetStore(CacheLayout layout, TimeProvider time, ILogger<AssetStore> log) : IAssetStore
{
    // snake_case y enums en minuscula: asi queda igual que el manifest del prototipo.
    static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // sin \u00F1 en los titulos
        WriteIndented = true,
    };

    public bool Exists(string assetId) => File.Exists(layout.Paths(assetId).Manifest);

    // --- Manifest ---------------------------------------------------------------

    public async Task<AssetManifest?> ReadManifestAsync(string assetId, CancellationToken ct = default)
    {
        try
        {
            var json = await File.ReadAllTextAsync(layout.Paths(assetId).Manifest, ct);
            return JsonSerializer.Deserialize<AssetManifest>(json, ManifestJson);
        }
        // Van primero: ambas heredan de IOException y el filtro de abajo las capturaria.
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            log.LogWarning(ex, "manifest ilegible: {AssetId}", assetId);
            return null;
        }
    }

    // Atomico: se escribe a un temporal y se renombra. El builder lo reescribe mientras las
    // requests lo leen; sin el rename un GET podria toparse con un JSON a medio escribir.
    public async Task WriteManifestAsync(string assetId, AssetManifest manifest, CancellationToken ct = default)
    {
        var path = layout.Paths(assetId).Manifest;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Nombre unico: dos builds guardando a la vez no pisan el mismo temporal.
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(manifest, ManifestJson), ct);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            File.Delete(temp); // no lanza si no existe
            throw;
        }
    }

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
            if (await ReadManifestAsync(id, ct) is { Pinned: true }) continue;
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