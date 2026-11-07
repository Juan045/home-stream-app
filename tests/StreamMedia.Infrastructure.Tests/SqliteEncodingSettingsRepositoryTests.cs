using Microsoft.Data.Sqlite;
using StreamMedia.Domain.Entities;
using StreamMedia.Infrastructure.Database;
using StreamMedia.Infrastructure.Encoder;

public class SqliteEncodingSettingsRepositoryTests : IDisposable
{
    readonly string _directory = Directory.CreateTempSubdirectory("stream-media-tests-").FullName;
    readonly SqliteConnectionFactory _factory;

    public SqliteEncodingSettingsRepositoryTests()
    {
        _factory = new SqliteConnectionFactory(
            new DatabaseOptions { Path = Path.Combine(_directory, "media.sqlite") });
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

    async Task<long> CountAsync(string table)
    {
        using var connection = await _factory.OpenConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Sin_fila_rigen_h264_en_vivo_y_av1_en_biblioteca()
    {
        await InitializeAsync();

        var settings = await new SqliteEncodingSettingsRepository(_factory).GetAsync();

        Assert.Equal(new EncodingSettings("h264", "av1"), settings);
    }

    // Otra instancia del repositorio: lo que se lee tiene que venir de la base.
    [Fact]
    public async Task Lo_guardado_se_lee_desde_la_base()
    {
        await InitializeAsync();

        await new SqliteEncodingSettingsRepository(_factory).SaveAsync(new EncodingSettings("av1", "h264"));
        var settings = await new SqliteEncodingSettingsRepository(_factory).GetAsync();

        Assert.Equal(new EncodingSettings("av1", "h264"), settings);
    }

    [Fact]
    public async Task Guardar_dos_veces_deja_una_fila_con_el_ultimo_valor()
    {
        await InitializeAsync();
        var repository = new SqliteEncodingSettingsRepository(_factory);

        await repository.SaveAsync(new EncodingSettings("av1", "av1"));
        await repository.SaveAsync(new EncodingSettings("h264", "h264"));

        Assert.Equal(new EncodingSettings("h264", "h264"), await repository.GetAsync());
        Assert.Equal(1, await CountAsync("encoding_settings"));
    }

    // Lo garantiza la base y no el repositorio: "una sola fila" no depende de quien escriba.
    [Fact]
    public async Task La_tabla_rechaza_una_segunda_fila()
    {
        await InitializeAsync();

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
            "INSERT INTO encoding_settings (id, live_codec, archive_codec) VALUES (2, 'h264', 'av1')"));
    }

    // S1: no hay migracion. Una base anterior a esta tabla la recibe al arrancar, sin perder las fichas.
    [Fact]
    public async Task Una_base_existente_recibe_la_tabla_al_inicializar()
    {
        await ExecuteAsync("""
            CREATE TABLE media (id_media TEXT PRIMARY KEY, title TEXT NOT NULL);
            INSERT INTO media (id_media, title) VALUES ('1', 'Pelicula');
            """);

        await InitializeAsync();
        var repository = new SqliteEncodingSettingsRepository(_factory);
        await repository.SaveAsync(new EncodingSettings("av1", "h264"));

        Assert.Equal(new EncodingSettings("av1", "h264"), await repository.GetAsync());
        Assert.Equal(1, await CountAsync("media"));
    }

    // Los valores viajan como parametros: el repositorio no valida, asi que no pueden armar SQL.
    [Fact]
    public async Task Un_valor_con_comillas_se_guarda_literal()
    {
        await InitializeAsync();
        var repository = new SqliteEncodingSettingsRepository(_factory);
        var hostile = new EncodingSettings("x'; DROP TABLE encoding_settings; --", "h264");

        await repository.SaveAsync(hostile);

        Assert.Equal(hostile, await repository.GetAsync());
    }

    public void Dispose()
    {
        // El pool mantiene abierto el archivo: en Windows no se podria borrar.
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }
}
