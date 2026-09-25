using System.Text.Json;
using Microsoft.Data.Sqlite;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Domain.Entities;
using StreamMedia.Infrastructure.Database;

namespace StreamMedia.Infrastructure.Media;

public sealed class SqliteMediaRepository : IMediaRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteMediaRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Domain.Entities.Media?> FindByPathAsync(string relativePath, CancellationToken ct)
    {
        var pathKey = NormalizePathKey(relativePath);

        using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id_media, file_path, title, year, synopsis, info
            FROM media
            WHERE path_key = $pathKey
            """;
        command.Parameters.AddWithValue("$pathKey", pathKey);

        using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return ReadMedia(reader);
    }

    public async Task<Domain.Entities.Media?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM Media WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", id.ToString());

        using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return ReadMedia(reader);
    }

    public async Task AddAsync(Domain.Entities.Media media, CancellationToken ct)
    {
        var pathKey = NormalizePathKey(media.FilePath);
        var fileName = System.IO.Path.GetFileName(media.FilePath);
        var infoJson = JsonSerializer.Serialize(media.SourceInfo);
        var now = DateTime.UtcNow.ToString("O"); // ISO 8601 format

        using var connection = await _connectionFactory.OpenConnectionAsync(ct);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO media
                (id_media, file_path, path_key, file_name, asset_id, duration, info,
                 title, kind, year, synopsis, genres, notes, in_list, created_at, updated_at)
            VALUES
                ($id, $filePath, $pathKey, $fileName, NULL, $duration, $info,
                 $title, 'film', $year, $synopsis, NULL, NULL, 0, $now, $now)
            """;
        
        command.Parameters.AddWithValue("$id", media.Id.ToString());
        command.Parameters.AddWithValue("$filePath", media.FilePath);
        command.Parameters.AddWithValue("$pathKey", pathKey);
        command.Parameters.AddWithValue("$fileName", fileName);
        command.Parameters.AddWithValue("$duration", media.SourceInfo.Duration);
        command.Parameters.AddWithValue("$info", infoJson);
        command.Parameters.AddWithValue("$title", media.Title);
        command.Parameters.AddWithValue("$year", (object?)media.Year ?? DBNull.Value);
        command.Parameters.AddWithValue("$synopsis", (object?)media.Synopsis ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(ct);
    }

    private Domain.Entities.Media ReadMedia(SqliteDataReader reader)
    {
        var id = Guid.Parse(reader.GetString(reader.GetOrdinal("id_media")));
        var filePath = reader.GetString(reader.GetOrdinal("file_path"));
        var title = reader.GetString(reader.GetOrdinal("title"));

        var yearOrdinal = reader.GetOrdinal("year");
        int? year = reader.IsDBNull(yearOrdinal) ? null : reader.GetInt32(yearOrdinal);

        var synopsisOrdinal = reader.GetOrdinal("synopsis");
        string? synopsis = reader.IsDBNull(synopsisOrdinal) ? null : reader.GetString(synopsisOrdinal);

        var infoJson = reader.GetString(reader.GetOrdinal("info"));
        var sourceInfo = JsonSerializer.Deserialize<SourceInfo>(infoJson)!;

        return Domain.Entities.Media.Rehydrate(id, filePath, title, year, synopsis, sourceInfo);
    }

    // Normaliza separadores y mayusculas/minusculas para que "Films/Dune.mkv"
    // y "films/dune.mkv" no generen dos fichas distintas. Coincide con el
    // proposito de path_key en el schema (columna UNIQUE).
    private static string NormalizePathKey(string filePath) =>
        filePath.Replace('\\', '/').ToLowerInvariant();
}