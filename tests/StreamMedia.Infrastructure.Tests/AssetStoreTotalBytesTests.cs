using Microsoft.Extensions.Logging.Abstractions;
using StreamMedia.Infrastructure.Cache;

// Lo que informa GET /admin/cache: todo lo que hay bajo Cache:Dir, sobre un directorio real.
public class AssetStoreTotalBytesTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stream-media-tests-").FullName;

    AssetStore NewStore()
        => new(new CacheOptions { Dir = _root }, TimeProvider.System, NullLogger<AssetStore>.Instance);

    static void WriteBytes(string path, int count)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[count]);
    }

    [Fact]
    public async Task Un_cache_vacio_ocupa_cero()
    {
        var store = NewStore();

        Assert.Equal(0, await store.TotalBytesAsync());
    }

    // Cuenta tambien manifest.json y .last-access: ocupan disco aunque no sean segmentos.
    [Fact]
    public async Task Suma_los_archivos_anidados_de_todos_los_assets()
    {
        var store = NewStore();

        var first = store.Paths("asset-uno");
        WriteBytes(Path.Combine(first.Video, "init.mp4"), 100);
        WriteBytes(Path.Combine(first.Video, "seg_00000.m4s"), 1000);
        WriteBytes(Path.Combine(first.Audio(0), "seg_00000.m4s"), 200);
        WriteBytes(Path.Combine(first.Subs, "0.vtt"), 30);
        WriteBytes(first.Manifest, 40);
        WriteBytes(first.AccessMarker, 5);

        var second = store.Paths("asset-dos");
        WriteBytes(Path.Combine(second.Audio(1), "seg_00000.m4s"), 7);

        Assert.Equal(100 + 1000 + 200 + 30 + 40 + 5 + 7, await store.TotalBytesAsync());
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
