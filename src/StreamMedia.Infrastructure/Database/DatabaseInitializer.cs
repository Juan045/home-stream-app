using Microsoft.Data.Sqlite;

namespace StreamMedia.Infrastructure.Database;

public sealed class DatabaseInitializer
{
    private readonly SqliteConnectionFactory _connectionFactory;

    private readonly string _schemaFilePath;

    public DatabaseInitializer(SqliteConnectionFactory connectionFactory, string schemaFilePath)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaFilePath);

        _connectionFactory = connectionFactory;
        _schemaFilePath = schemaFilePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        var schemaFilePath = System.IO.Path.IsPathFullyQualified(_schemaFilePath)
            ? _schemaFilePath
            : System.IO.Path.Combine(AppContext.BaseDirectory, _schemaFilePath);

        if (!File.Exists(schemaFilePath))
        {
            throw new FileNotFoundException(
                $"El archivo de esquema SQL no se encontro en la ruta: {schemaFilePath}",
                schemaFilePath);
        }

        var schemaSql = await File.ReadAllTextAsync(schemaFilePath, cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = schemaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await EnsureStrategyColumnAsync(connection, cancellationToken);
    }

    // Migracion: BDs creadas antes de que existiera strategy. Idempotente.
    // La definicion debe coincidir con la de schema.sql.
    private static async Task EnsureStrategyColumnAsync(SqliteConnection connection, CancellationToken ct)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('media') WHERE name = 'strategy'";
        if (Convert.ToInt32(await check.ExecuteScalarAsync(ct)) > 0) return;

        using var alter = connection.CreateCommand();
        alter.CommandText = """
            ALTER TABLE media
            ADD COLUMN strategy TEXT NOT NULL DEFAULT 'transcode' CHECK (strategy IN ('remux','transcode'))
            """;
        await alter.ExecuteNonQueryAsync(ct);
    }
}
