using Microsoft.Data.Sqlite;

namespace StreamMedia.Infrastructure.Database;

/// <summary>
/// Crea conexiones SQLite para el catalogo de StreamMedia.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Path))
        {
            throw new ArgumentException(
                "La ruta de la base de datos es obligatoria.",
                nameof(options));
        }

        DatabasePath = System.IO.Path.GetFullPath(options.Path);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = true
        }.ToString();
    }

    public string DatabasePath { get; }

    /// <summary>
    /// Crea una conexion cerrada. Quien la consume debe abrirla y liberarla.
    /// </summary>
    public SqliteConnection CreateConnection() => new(_connectionString);

    /// <summary>
    /// Crea el directorio de la base si hace falta y devuelve una conexion abierta.
    /// Quien la consume debe liberarla.
    /// </summary>
    public async Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var directory = System.IO.Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = CreateConnection();

        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
