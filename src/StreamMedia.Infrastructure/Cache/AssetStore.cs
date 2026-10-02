using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StreamMedia.Application.Streaming;
using StreamMedia.Domain.DTO;

namespace StreamMedia.Infrastructure.Cache;

public sealed class AssetStore : IAssetStore
{
    const int AssetIdLength = 16;

    // snake_case y enums en minuscula: asi queda igual que el manifest del prototipo.
    static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // sin \u00F1 en los titulos
        WriteIndented = true,
    };

    readonly string _root;
    readonly TimeProvider _time;
    readonly ILogger<AssetStore> _log;

    // Constructor clasico (no primario): hay que hacer algo al construir.
    public AssetStore(CacheOptions options, TimeProvider time, ILogger<AssetStore> log)
    {
        _root = Path.GetFullPath(options.Dir);
        _time = time;
        _log = log;
        Directory.CreateDirectory(_root); // idempotente; el cache puede ser un mount
    }

    // --- Identidad y layout ---------------------------------------------------

    public string AssetIdFor(string source)
    {
        var file = new FileInfo(source);
        var normalized = file.FullName;
        // Windows no distingue mayusculas: la misma pelicula con otra capitalizacion es el mismo asset.
        if (OperatingSystem.IsWindows()) normalized = normalized.ToLowerInvariant();

        // ponytail: no resuelve symlinks (Python .resolve() si); dos rutas al mismo archivo
        // generan dos assets. Inofensivo salvo por el disco.
        var raw = $"{normalized}|{file.LastWriteTimeUtc.Ticks}|{file.Length}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(hash)[..AssetIdLength];
    }

    public AssetPaths Paths(string assetId) => new(Path.Combine(_root, assetId));

    public AssetPaths Prepare(string assetId)
    {
        var paths = Paths(assetId);
        Directory.CreateDirectory(paths.Video);
        Directory.CreateDirectory(paths.Subs);
        Directory.CreateDirectory(paths.AudioRoot);
        // Sin marcador seria el mas viejo del LRU (0.0) y el primero en irse.
        File.WriteAllText(paths.AccessMarker, Now());
        return paths;
    }

    public string PrepareAudio(string assetId, int track)
    {
        var directory = Paths(assetId).Audio(track);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public bool Exists(string assetId) => File.Exists(Paths(assetId).Manifest);

    public int CountSegments(string directory)
        => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, HlsLayout.SegmentGlob).Count()
            : 0;

    // --- Manifest ---------------------------------------------------------------

    public async Task<AssetManifest?> ReadManifestAsync(string assetId, CancellationToken ct = default)
    {
        try
        {
            var json = await File.ReadAllTextAsync(Paths(assetId).Manifest, ct);
            return JsonSerializer.Deserialize<AssetManifest>(json, ManifestJson);
        }
        // Van primero: ambas heredan de IOException y el filtro de abajo las capturaria.
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _log.LogWarning(ex, "manifest ilegible: {AssetId}", assetId);
            return null;
        }
    }

    // Atomico: se escribe a un temporal y se renombra. El builder lo reescribe mientras las
    // requests lo leen; sin el rename un GET podria toparse con un JSON a medio escribir.
    public async Task WriteManifestAsync(string assetId, AssetManifest manifest, CancellationToken ct = default)
    {
        var path = Paths(assetId).Manifest;
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
        var paths = Paths(assetId);
        if (!Directory.Exists(paths.Root)) return; // no resucitar un asset ya borrado
        await File.WriteAllTextAsync(paths.AccessMarker, Now(), ct);
    }

    // 0.0 si nunca se marco (el mas viejo del LRU).
    public double LastAccess(string assetId)
    {
        try
        {
            var text = File.ReadAllText(Paths(assetId).AccessMarker);
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                ? seconds
                : 0.0;
        }
        catch (IOException) { return 0.0; }
    }

    public Task<long> TotalBytesAsync(CancellationToken ct = default)
        => Task.Run(() => TreeSize(_root), ct);

    // --- Limpieza -----------------------------------------------------------------

    public bool Remove(string assetId)
    {
        try
        {
            Directory.Delete(Paths(assetId).Root, recursive: true);
        }
        catch (DirectoryNotFoundException) { return true; } // ya no estaba: da igual
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // En Windows un segmento abierto por FFmpeg no se puede borrar.
            _log.LogWarning(ex, "no se pudo borrar el asset {AssetId}", assetId);
            return false;
        }

        _log.LogInformation("asset borrado {AssetId}", assetId);
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

            var size = await Task.Run(() => TreeSize(Paths(id).Root), ct);
            if (!await Task.Run(() => Remove(id), ct)) continue;

            total -= size;
            removed.Add(id);
        }

        if (removed.Count > 0)
            _log.LogInformation("gc completado: {Removed} assets, {TotalBytes} bytes", removed.Count, total);

        return removed;
    }

    // --- Internos -----------------------------------------------------------------

    IEnumerable<string> AssetIds()
        => Directory.EnumerateDirectories(_root).Select(d => Path.GetFileName(d)).Order();

    // Segundos Unix con decimales, igual que el marcador del prototipo.
    string Now()
        => (_time.GetUtcNow().ToUnixTimeMilliseconds() / 1000.0).ToString("F3", CultureInfo.InvariantCulture);

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