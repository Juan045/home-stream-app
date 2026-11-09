using Microsoft.Extensions.Logging.Abstractions;
using StreamMedia.Application.Cache;
using StreamMedia.Domain.Entities;
using StreamMedia.Infrastructure.Cache;

// Caracterizan el LRU, el touch y la creacion de carpetas. Se escribieron antes de partir el acceso
// al cache y siguen afirmando lo mismo sobre CacheSpace.
public class CacheSpaceGcTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("stream-media-tests-").FullName;

    CacheLayout Layout => new(_root);

    // El pin lo lee de los manifests: el repositorio real, sobre el mismo _root.
    AssetRepository NewRepository() => new(Layout, NullLogger<AssetRepository>.Instance);

    CacheSpace NewSpace(TimeProvider? time = null)
        => new(Layout, NewRepository(), time ?? TimeProvider.System, NullLogger<CacheSpace>.Instance);

    // Se crean en un orden (c, a, b) que no es ni el del nombre ni el del acceso: asi se nota
    // si el GC ordena por nombre o por fecha de creacion en vez de por el .last-access.
    CacheSpace SpaceWithThreeAssets()
    {
        var space = NewSpace();
        WriteAsset("c", "200.000");
        WriteAsset("a", "300.000"); // el mas nuevo
        WriteAsset("b", "100.000"); // el mas viejo
        return space;
    }

    void WriteAsset(string id, string lastAccess)
    {
        var paths = Layout.Paths(id);
        Directory.CreateDirectory(paths.Video);
        File.WriteAllBytes(Path.Combine(paths.Video, "seg-00000.m4s"), new byte[1000]);
        File.WriteAllText(paths.AccessMarker, lastAccess);
    }

    // La carpeta, no el manifest: AssetRepository.Exists mira el manifest.
    bool FolderExists(string id) => Directory.Exists(Path.Combine(_root, id));

    [Fact]
    public async Task Con_el_total_por_debajo_del_maximo_no_borra_nada()
    {
        var space = SpaceWithThreeAssets();
        var total = await space.TotalBytesAsync();

        var removed = await space.CollectAsync(total + 1);

        Assert.Empty(removed);
        Assert.True(FolderExists("a"));
        Assert.True(FolderExists("b"));
        Assert.True(FolderExists("c"));
    }

    // Un byte de mas alcanza para obligar a borrar, y borrar uno solo ya lo baja del maximo.
    [Fact]
    public async Task Borra_el_de_acceso_mas_viejo_y_se_detiene_al_bajar_del_maximo()
    {
        var space = SpaceWithThreeAssets();
        var total = await space.TotalBytesAsync();

        await space.CollectAsync(total - 1);

        Assert.False(FolderExists("b"));
        Assert.True(FolderExists("c"));
        Assert.True(FolderExists("a"));
    }

    // Un pineado no se borra nunca: perderlo es perder horas de codificacion.
    [Fact]
    public async Task Saltea_los_pineados()
    {
        var space = SpaceWithThreeAssets();
        await NewRepository().WriteManifestAsync("b", new AssetManifest
        {
            AssetId = "b",
            Info = new SourceInfo(100, "h264", 1920, 1080, [], []),
            Pinned = true,
        });
        var total = await space.TotalBytesAsync();

        await space.CollectAsync(total - 1);

        Assert.True(FolderExists("b"));
        Assert.False(FolderExists("c"));
        Assert.True(FolderExists("a"));
    }

    // keep son los assets con sesiones o builds vivos.
    [Fact]
    public async Task Saltea_los_de_keep()
    {
        var space = SpaceWithThreeAssets();
        var total = await space.TotalBytesAsync();

        await space.CollectAsync(total - 1, new HashSet<string> { "b" });

        Assert.True(FolderExists("b"));
        Assert.False(FolderExists("c"));
        Assert.True(FolderExists("a"));
    }

    // Con maximo cero se van todos, en orden de acceso.
    [Fact]
    public async Task Devuelve_los_ids_borrados_en_orden_de_acceso()
    {
        var space = SpaceWithThreeAssets();

        var removed = await space.CollectAsync(0);

        Assert.Equal(new[] { "b", "c", "a" }, removed);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    // Un heartbeat tardio no puede resucitar un asset que el GC ya borro.
    [Fact]
    public async Task Touch_sobre_un_asset_sin_carpeta_no_crea_nada()
    {
        var space = NewSpace();

        await space.TouchAsync("borrado");

        Assert.False(FolderExists("borrado"));
    }

    [Fact]
    public async Task Crear_carpetas_y_touch_dejan_las_carpetas_y_el_marcador()
    {
        var space = NewSpace(new FixedTime(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_500)));
        var paths = Layout.Paths("asset");

        new HlsOutput(Layout).CreateDirectories("asset");
        await space.TouchAsync("asset");

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
