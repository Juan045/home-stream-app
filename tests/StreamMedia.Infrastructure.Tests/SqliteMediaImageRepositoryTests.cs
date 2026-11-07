using Microsoft.Data.Sqlite;
using StreamMedia.Application.Images;
using StreamMedia.Infrastructure.Database;
using StreamMedia.Infrastructure.Images;

public class SqliteMediaImageRepositoryTests : IDisposable
{
    // Con offset y ticks: el instante tiene que volver exacto, no redondeado a segundos.
    static readonly DateTimeOffset At = new DateTimeOffset(2026, 11, 7, 9, 30, 15, TimeSpan.FromHours(-3)).AddTicks(1_234_567);

    readonly string _directory = Directory.CreateTempSubdirectory("stream-media-tests-").FullName;
    readonly SqliteConnectionFactory _factory;
    readonly SqliteMediaImageRepository _repository;

    public SqliteMediaImageRepositoryTests()
    {
        _factory = new SqliteConnectionFactory(
            new DatabaseOptions { Path = Path.Combine(_directory, "media.sqlite") });
        _repository = new SqliteMediaImageRepository(_factory);
    }

    // Con el schema.sql real: un esquema copiado aca no detectaria un error en el verdadero.
    Task InitializeAsync()
        => new DatabaseInitializer(_factory, new DatabaseOptions().SchemaFile).InitializeAsync();

    async Task ExecuteAsync(string sql)
    {
        using var connection = await _factory.OpenConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    async Task<long> ScalarAsync(string sql)
    {
        using var connection = await _factory.OpenConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    // Hay clave foranea: una imagen necesita su ficha. path_key es UNIQUE, por eso sale del id.
    async Task<Guid> InsertMediaAsync()
    {
        var id = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO media (id_media, file_path, path_key, file_name, title, created_at, updated_at)
            VALUES ('{id}', '{id}.mkv', '{id}.mkv', '{id}.mkv', 'A', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')
            """);
        return id;
    }

    static void AssertSame(MediaImage expected, MediaImage? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.ContentType, actual.ContentType);
        Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
        Assert.True(expected.Data.SequenceEqual(actual.Data));
    }

    [Fact]
    public async Task Lo_guardado_se_lee_igual()
    {
        await InitializeAsync();
        var id = await InsertMediaAsync();
        var image = new MediaImage("image/webp", [.. "RIFF"u8, 1, 2, 3, 4, .. "WEBP"u8, 0, 255, 128], At);

        await _repository.SaveAsync(id, MediaImageRole.Poster, image);

        // Otra instancia: lo que se lee tiene que venir de la base.
        AssertSame(image, await new SqliteMediaImageRepository(_factory).GetAsync(id, MediaImageRole.Poster));
    }

    [Fact]
    public async Task Sin_imagen_se_lee_null()
    {
        await InitializeAsync();
        var id = await InsertMediaAsync();

        Assert.Null(await _repository.GetAsync(id, MediaImageRole.Poster));
    }

    [Fact]
    public async Task Guardar_dos_veces_el_mismo_rol_reemplaza_y_deja_una_fila()
    {
        await InitializeAsync();
        var id = await InsertMediaAsync();
        var second = new MediaImage("image/webp", [4, 5, 6, 7], At.AddMinutes(1));

        await _repository.SaveAsync(id, MediaImageRole.Poster, new MediaImage("image/jpeg", [1, 2, 3], At));
        await _repository.SaveAsync(id, MediaImageRole.Poster, second);

        AssertSame(second, await _repository.GetAsync(id, MediaImageRole.Poster));
        Assert.Equal(1, await ScalarAsync($"SELECT COUNT(*) FROM media_images WHERE id_media = '{id}'"));
    }

    [Fact]
    public async Task Poster_y_backdrop_son_independientes()
    {
        await InitializeAsync();
        var id = await InsertMediaAsync();
        var poster = new MediaImage("image/jpeg", [1, 2, 3], At);
        var backdrop = new MediaImage("image/webp", [9, 8, 7], At.AddSeconds(5));

        await _repository.SaveAsync(id, MediaImageRole.Poster, poster);
        await _repository.SaveAsync(id, MediaImageRole.Backdrop, backdrop);

        AssertSame(poster, await _repository.GetAsync(id, MediaImageRole.Poster));
        AssertSame(backdrop, await _repository.GetAsync(id, MediaImageRole.Backdrop));
    }

    [Fact]
    public async Task Borrar_el_poster_deja_el_backdrop()
    {
        await InitializeAsync();
        var id = await InsertMediaAsync();
        var backdrop = new MediaImage("image/webp", [9, 8, 7], At);
        await _repository.SaveAsync(id, MediaImageRole.Poster, new MediaImage("image/jpeg", [1, 2, 3], At));
        await _repository.SaveAsync(id, MediaImageRole.Backdrop, backdrop);

        await _repository.DeleteAsync(id, MediaImageRole.Poster);

        Assert.Null(await _repository.GetAsync(id, MediaImageRole.Poster));
        AssertSame(backdrop, await _repository.GetAsync(id, MediaImageRole.Backdrop));
    }

    // Idempotente: ni ficha ni imagen existen y no falla.
    [Fact]
    public async Task Borrar_algo_que_no_existe_no_lanza()
    {
        await InitializeAsync();

        await _repository.DeleteAsync(Guid.NewGuid(), MediaImageRole.Backdrop);
    }

    [Fact]
    public async Task Las_imagenes_de_dos_fichas_no_se_mezclan()
    {
        await InitializeAsync();
        var first = await InsertMediaAsync();
        var second = await InsertMediaAsync();
        var a = new MediaImage("image/jpeg", [1, 1, 1], At);
        var b = new MediaImage("image/webp", [2, 2, 2], At.AddHours(1));

        await _repository.SaveAsync(first, MediaImageRole.Poster, a);
        await _repository.SaveAsync(second, MediaImageRole.Poster, b);

        AssertSame(a, await _repository.GetAsync(first, MediaImageRole.Poster));
        AssertSame(b, await _repository.GetAsync(second, MediaImageRole.Poster));
    }

    // Validar es del use case: el repositorio guarda lo que le dan.
    [Fact]
    public async Task Bytes_que_no_son_imagen_se_guardan_tal_cual()
    {
        await InitializeAsync();
        var id = await InsertMediaAsync();
        var junk = new MediaImage("text/plain", [0, 0, 0, 1, 255], At);

        await _repository.SaveAsync(id, MediaImageRole.Backdrop, junk);

        AssertSame(junk, await _repository.GetAsync(id, MediaImageRole.Backdrop));
    }

    // Sin migracion: una base anterior a la tabla la recibe al arrancar, sin perder las fichas.
    [Fact]
    public async Task Una_base_existente_recibe_la_tabla_al_inicializar()
    {
        var id = Guid.NewGuid();
        await ExecuteAsync($"""
            CREATE TABLE media (id_media TEXT PRIMARY KEY, title TEXT NOT NULL);
            INSERT INTO media (id_media, title) VALUES ('{id}', 'Pelicula');
            """);
        var image = new MediaImage("image/jpeg", [1, 2, 3], At);

        await InitializeAsync();
        await _repository.SaveAsync(id, MediaImageRole.Poster, image);

        AssertSame(image, await _repository.GetAsync(id, MediaImageRole.Poster));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM media"));
    }

    // Lo garantiza la base con la clave foranea, no el repositorio.
    [Fact]
    public async Task Guardar_para_una_ficha_inexistente_falla()
    {
        await InitializeAsync();

        await Assert.ThrowsAsync<SqliteException>(() => _repository.SaveAsync(
            Guid.NewGuid(), MediaImageRole.Poster, new MediaImage("image/jpeg", [1, 2, 3], At)));
    }

    public void Dispose()
    {
        // El pool mantiene abierto el archivo: en Windows no se podria borrar.
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
