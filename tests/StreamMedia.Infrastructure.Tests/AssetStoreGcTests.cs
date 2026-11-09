using Microsoft.Extensions.Logging.Abstractions;
using StreamMedia.Application.Streaming;
using StreamMedia.Domain.Entities;
using StreamMedia.Infrastructure.Cache;

// Caracterizan el LRU, el touch y el Prepare de hoy, antes de partir AssetStore.
public class AssetStoreGcTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stream-media-tests-").FullName;

    AssetStore NewStore(TimeProvider? time = null)
        => new(new CacheOptions { Dir = _root }, time ?? TimeProvider.System, NullLogger<AssetStore>.Instance);

    // Se crean en un orden (c, a, b) que no es ni el del nombre ni el del acceso: asi se nota
    // si el GC ordena por nombre o por fecha de creacion en vez de por el .last-access.
    AssetStore StoreWithThreeAssets()
    {
        var store = NewStore();
        WriteAsset(store, "c", "200.000");
        WriteAsset(store, "a", "300.000"); // el mas nuevo
        WriteAsset(store, "b", "100.000"); // el mas viejo
        return store;
    }

    static void WriteAsset(AssetStore store, string id, string lastAccess)
    {
        var paths = store.Paths(id);
        Directory.CreateDirectory(paths.Video);
        File.WriteAllBytes(Path.Combine(paths.Video, "seg-00000.m4s"), new byte[1000]);
        File.WriteAllText(paths.AccessMarker, lastAccess);
    }

    // La carpeta, no el manifest: AssetStore.Exists mira el manifest.
    bool FolderExists(string id) => Directory.Exists(Path.Combine(_root, id));

    [Fact]
    public async Task Con_el_total_por_debajo_del_maximo_no_borra_nada()
    {
        var store = StoreWithThreeAssets();
        var total = await store.TotalBytesAsync();

        var removed = await store.CollectAsync(total + 1);

        Assert.Empty(removed);
        Assert.True(FolderExists("a"));
        Assert.True(FolderExists("b"));
        Assert.True(FolderExists("c"));
    }

    // Un byte de mas alcanza para obligar a borrar, y borrar uno solo ya lo baja del maximo.
    [Fact]
    public async Task Borra_el_de_acceso_mas_viejo_y_se_detiene_al_bajar_del_maximo()
    {
        var store = StoreWithThreeAssets();
        var total = await store.TotalBytesAsync();

        await store.CollectAsync(total - 1);

        Assert.False(FolderExists("b"));
        Assert.True(FolderExists("c"));
        Assert.True(FolderExists("a"));
    }

    // Un pineado no se borra nunca: perderlo es perder horas de codificacion.
    [Fact]
    public async Task Saltea_los_pineados()
    {
        var store = StoreWithThreeAssets();
        await store.WriteManifestAsync("b", new AssetManifest
        {
            AssetId = "b",
            Info = new SourceInfo(100, "h264", 1920, 1080, [], []),
            Pinned = true,
        });
        var total = await store.TotalBytesAsync();

        await store.CollectAsync(total - 1);

        Assert.True(FolderExists("b"));
        Assert.False(FolderExists("c"));
        Assert.True(FolderExists("a"));
    }

    // keep son los assets con sesiones o builds vivos.
    [Fact]
    public async Task Saltea_los_de_keep()
    {
        var store = StoreWithThreeAssets();
        var total = await store.TotalBytesAsync();

        await store.CollectAsync(total - 1, new HashSet<string> { "b" });

        Assert.True(FolderExists("b"));
        Assert.False(FolderExists("c"));
        Assert.True(FolderExists("a"));
    }

    // Con maximo cero se van todos, en orden de acceso.
    [Fact]
    public async Task Devuelve_los_ids_borrados_en_orden_de_acceso()
    {
        var store = StoreWithThreeAssets();

        var removed = await store.CollectAsync(0);

        Assert.Equal(new[] { "b", "c", "a" }, removed);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    // Un heartbeat tardio no puede resucitar un asset que el GC ya borro.
    [Fact]
    public async Task Touch_sobre_un_asset_sin_carpeta_no_crea_nada()
    {
        var store = NewStore();

        await store.TouchAsync("borrado");

        Assert.False(FolderExists("borrado"));
    }

    [Fact]
    public void Prepare_crea_las_carpetas_y_escribe_el_marcador()
    {
        var store = NewStore(new FixedTime(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_500)));

        var paths = store.Prepare("asset");

        Assert.True(Directory.Exists(paths.Video));
        Assert.True(Directory.Exists(paths.Subs));
        Assert.True(Directory.Exists(paths.AudioRoot));
        // Segundos Unix con tres decimales: es lo que LastAccess vuelve a leer.
        Assert.Equal("1700000000.500", File.ReadAllText(paths.AccessMarker));
    }

    sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
