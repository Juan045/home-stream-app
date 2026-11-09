using Microsoft.Extensions.Logging.Abstractions;
using StreamMedia.Application.Cache;
using StreamMedia.Domain.Entities;
using StreamMedia.Infrastructure.Cache;

public class AssetRepositoryManifestErrorsTests : IDisposable
{
    const string AssetId = "asset";

    readonly string _root = Directory.CreateTempSubdirectory("stream-media-tests-").FullName;

    CacheLayout Layout => new(_root);

    AssetRepository NewStore()
        => new(Layout, NullLogger<AssetRepository>.Instance);

    [Fact]
    public async Task Los_errores_sobreviven_a_escribir_y_leer_el_manifest()
    {
        var store = NewStore();
        var manifest = new AssetManifest
        {
            AssetId = AssetId,
            Info = new SourceInfo(100, "h264", 1920, 1080, [], []),
            VideoError = "error de video",
            AudioErrors = new() { [1] = "error de audio" },
            SubtitleErrors = new() { [2] = "error de subtitulo" },
        };

        await store.WriteManifestAsync(AssetId, manifest);
        var read = await store.ReadManifestAsync(AssetId);

        Assert.NotNull(read);
        Assert.Equal("error de video", read.VideoError);
        Assert.Equal("error de audio", read.AudioErrors[1]);
        Assert.Equal("error de subtitulo", read.SubtitleErrors[2]);

        // Las claves son parte del formato en disco: cambiarlas deja huerfanos los manifests existentes.
        var json = await File.ReadAllTextAsync(Layout.Paths(AssetId).Manifest);
        Assert.Contains("\"video_error\"", json);
        Assert.Contains("\"audio_errors\"", json);
        Assert.Contains("\"subtitle_errors\"", json);
    }

    // Un manifest escrito antes de estos campos tiene que seguir leyendose: es el cache que ya existe.
    [Fact]
    public async Task Un_manifest_sin_errores_se_lee_con_valores_vacios()
    {
        var store = NewStore();
        var path = Layout.Paths(AssetId).Manifest;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """
            {
              "asset_id": "asset",
              "source": "/media/pelicula.mkv",
              "duration": 100,
              "strategy": "remux",
              "video": "ready",
              "audio": {},
              "subtitles": {},
              "subtitles_failed": [],
              "info": {
                "duration": 100,
                "video_codec": "h264",
                "width": 1920,
                "height": 1080,
                "audio_tracks": [],
                "subtitle_tracks": []
              },
              "pinned": false
            }
            """);

        var read = await store.ReadManifestAsync(AssetId);

        Assert.NotNull(read);
        Assert.Null(read.VideoError);
        Assert.Empty(read.AudioErrors);
        Assert.Empty(read.SubtitleErrors);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
