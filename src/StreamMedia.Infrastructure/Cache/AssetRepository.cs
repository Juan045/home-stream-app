using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StreamMedia.Application.Cache;
using StreamMedia.Application.Cache.Interfaces;
using StreamMedia.Domain.Entities;

namespace StreamMedia.Infrastructure.Cache;

// La raiz ya llega absoluta y creada: eso lo hace el registro de CacheLayout en Program.cs.
public sealed class AssetRepository(CacheLayout layout, ILogger<AssetRepository> log) : IAssetRepository
{
    // snake_case y enums en minuscula: asi queda igual que el manifest del prototipo.
    static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // sin \u00F1 en los titulos
        WriteIndented = true,
    };

    // --- Manifest ---------------------------------------------------------------

    public bool Exists(string assetId) => File.Exists(layout.Paths(assetId).Manifest);

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
}