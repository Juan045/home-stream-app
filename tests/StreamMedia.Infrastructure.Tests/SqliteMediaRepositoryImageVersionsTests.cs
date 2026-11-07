using Microsoft.Data.Sqlite;
using StreamMedia.Application.Images;
using StreamMedia.Application.Media;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Domain.Entities;
using StreamMedia.Infrastructure.Database;
using StreamMedia.Infrastructure.Images;
using StreamMedia.Infrastructure.Media;

// Cada lectura de fichas trae la version de su poster y su backdrop desde media_images.
public class SqliteMediaRepositoryImageVersionsTests : IDisposable
{
    // Con offset y ticks: la version tiene que volver exacta, es lo que arma el ?v= de la URL.
    static readonly DateTimeOffset T1 = new DateTimeOffset(2026, 11, 7, 9, 30, 15, TimeSpan.FromHours(-3)).AddTicks(1_234_567);
    static readonly DateTimeOffset T2 = new DateTimeOffset(2026, 11, 8, 22, 5, 0, TimeSpan.FromHours(2)).AddTicks(7_654_321);

    readonly string _directory = Directory.CreateTempSubdirectory("stream-media-tests-").FullName;
    readonly SqliteConnectionFactory _factory;
    readonly IMediaRepository _media;
    readonly SqliteMediaImageRepository _images;

    public SqliteMediaRepositoryImageVersionsTests()
    {
        _factory = new SqliteConnectionFactory(
            new DatabaseOptions { Path = Path.Combine(_directory, "media.sqlite") });
        _media = new SqliteMediaRepository(_factory);
        _images = new SqliteMediaImageRepository(_factory);
    }

    // Con el schema.sql real: un esquema copiado aca no detectaria un error en el verdadero.
    Task InitializeAsync()
        => new DatabaseInitializer(_factory, new DatabaseOptions().SchemaFile).InitializeAsync();

    async Task<Guid> AddMediaAsync(string path)
    {
        var ficha = Media.FromSource(path, new SourceInfo(100, "h264", 1920, 1080, [], []));
        await _media.AddAsync(ficha);
        return ficha.Id;
    }

    Task SaveAsync(Guid id, MediaImageRole role, DateTimeOffset at)
        => _images.SaveAsync(id, role, new MediaImage("image/jpeg", [0xFF, 0xD8, 0xFF], at));

    // Igualdad de DateTimeOffset compara solo el instante; aca importan tambien ticks y offset.
    static void AssertExact(DateTimeOffset expected, DateTimeOffset? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Ticks, actual.Value.Ticks);
        Assert.Equal(expected.Offset, actual.Value.Offset);
    }

    [Fact]
    public async Task Sin_imagenes_las_dos_versiones_son_null()
    {
        await InitializeAsync();
        var id = await AddMediaAsync("films/a.mkv");

        var ficha = await _media.GetByIdAsync(id);

        Assert.NotNull(ficha);
        Assert.Null(ficha.PosterUpdatedAt);
        Assert.Null(ficha.BackdropUpdatedAt);
    }

    [Fact]
    public async Task Con_poster_trae_su_instante_exacto_y_el_backdrop_queda_null()
    {
        await InitializeAsync();
        var id = await AddMediaAsync("films/a.mkv");
        await SaveAsync(id, MediaImageRole.Poster, T1);

        var ficha = await _media.GetByIdAsync(id);

        AssertExact(T1, ficha!.PosterUpdatedAt);
        Assert.Null(ficha.BackdropUpdatedAt);
    }

    [Fact]
    public async Task Con_las_dos_cada_una_trae_su_instante()
    {
        await InitializeAsync();
        var id = await AddMediaAsync("films/a.mkv");
        await SaveAsync(id, MediaImageRole.Poster, T1);
        await SaveAsync(id, MediaImageRole.Backdrop, T2);

        var ficha = await _media.GetByIdAsync(id);

        AssertExact(T1, ficha!.PosterUpdatedAt);
        AssertExact(T2, ficha.BackdropUpdatedAt);
    }

    [Fact]
    public async Task Reemplazar_el_poster_trae_la_version_nueva()
    {
        await InitializeAsync();
        var id = await AddMediaAsync("films/a.mkv");
        await SaveAsync(id, MediaImageRole.Poster, T1);
        await SaveAsync(id, MediaImageRole.Poster, T2);

        var ficha = await _media.GetByIdAsync(id);

        AssertExact(T2, ficha!.PosterUpdatedAt);
    }

    [Fact]
    public async Task Borrar_el_poster_vuelve_a_null_y_no_toca_el_backdrop()
    {
        await InitializeAsync();
        var id = await AddMediaAsync("films/a.mkv");
        await SaveAsync(id, MediaImageRole.Poster, T1);
        await SaveAsync(id, MediaImageRole.Backdrop, T2);

        await _images.DeleteAsync(id, MediaImageRole.Poster);
        var ficha = await _media.GetByIdAsync(id);

        Assert.Null(ficha!.PosterUpdatedAt);
        AssertExact(T2, ficha.BackdropUpdatedAt);
    }

    // Subconsulta y no JOIN: ninguna ficha se duplica ni hereda las versiones de otra.
    [Fact]
    public async Task El_listado_trae_cada_ficha_una_vez_con_sus_versiones()
    {
        await InitializeAsync();
        var conImagenes = await AddMediaAsync("films/a.mkv");
        var sinImagenes = await AddMediaAsync("films/b.mkv");
        await SaveAsync(conImagenes, MediaImageRole.Poster, T1);
        await SaveAsync(conImagenes, MediaImageRole.Backdrop, T2);

        var items = await _media.ListAsync(new MediaListQuery());

        Assert.Equal(2, items.Count);
        var a = Assert.Single(items, m => m.Id == conImagenes);
        var b = Assert.Single(items, m => m.Id == sinImagenes);
        AssertExact(T1, a.PosterUpdatedAt);
        AssertExact(T2, a.BackdropUpdatedAt);
        Assert.Null(b.PosterUpdatedAt);
        Assert.Null(b.BackdropUpdatedAt);
    }

    [Fact]
    public async Task El_total_no_cambia_al_guardar_imagenes()
    {
        await InitializeAsync();
        var conImagenes = await AddMediaAsync("films/a.mkv");
        await AddMediaAsync("films/b.mkv");
        var antes = await _media.CountAsync(new MediaListQuery());

        await SaveAsync(conImagenes, MediaImageRole.Poster, T1);
        await SaveAsync(conImagenes, MediaImageRole.Backdrop, T2);

        Assert.Equal(2, antes);
        Assert.Equal(2, await _media.CountAsync(new MediaListQuery()));
    }

    [Fact]
    public async Task Buscar_por_ruta_tambien_trae_la_version_del_poster()
    {
        await InitializeAsync();
        var id = await AddMediaAsync("films/a.mkv");
        await SaveAsync(id, MediaImageRole.Poster, T1);

        var ficha = await _media.FindByPathAsync("films/a.mkv");

        AssertExact(T1, ficha!.PosterUpdatedAt);
    }

    public void Dispose()
    {
        // El pool mantiene abierto el archivo: en Windows no se podria borrar.
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
