using System.Globalization;
using Microsoft.Data.Sqlite;
using StreamMedia.Application.Images;
using StreamMedia.Application.Images.Interfaces;
using StreamMedia.Infrastructure.Database;

namespace StreamMedia.Infrastructure.Images;

// Los bytes van como BLOB en la misma base que la ficha: una sola fuente de verdad, con transaccion.
public sealed class SqliteMediaImageRepository(SqliteConnectionFactory connectionFactory)
    : IMediaImageRepository
{
    // ponytail: el BLOB entero a memoria por request; con el tope de 1 MiB alcanza.
    // Si el tope sube mucho, servirlo en streaming con SqliteBlob.
    public async Task<MediaImage?> GetAsync(Guid idMedia, MediaImageRole role, CancellationToken ct = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(ct);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT content_type, data, updated_at
            FROM media_images
            WHERE id_media = $id AND role = $role
            """;
        AddKey(command, idMedia, role);

        using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new MediaImage(
            reader.GetString(0),
            reader.GetFieldValue<byte[]>(1),
            DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    public async Task SaveAsync(Guid idMedia, MediaImageRole role, MediaImage image, CancellationToken ct = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(ct);
        using var command = connection.CreateCommand();

        // Un solo upsert: tipo, bytes y fecha cambian juntos o no cambia nada.
        command.CommandText = """
            INSERT INTO media_images (id_media, role, content_type, data, updated_at)
            VALUES ($id, $role, $contentType, $data, $updatedAt)
            ON CONFLICT(id_media, role) DO UPDATE SET
                content_type = excluded.content_type,
                data = excluded.data,
                updated_at = excluded.updated_at
            """;
        AddKey(command, idMedia, role);
        command.Parameters.AddWithValue("$contentType", image.ContentType);
        command.Parameters.Add("$data", SqliteType.Blob).Value = image.Data;
        command.Parameters.AddWithValue("$updatedAt", image.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(Guid idMedia, MediaImageRole role, CancellationToken ct = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(ct);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM media_images WHERE id_media = $id AND role = $role";
        AddKey(command, idMedia, role);

        await command.ExecuteNonQueryAsync(ct);
    }

    // Mismo formato de id que SqliteMediaRepository (Guid.ToString()), si no la FK no matchea.
    private static void AddKey(SqliteCommand command, Guid idMedia, MediaImageRole role)
    {
        command.Parameters.AddWithValue("$id", idMedia.ToString());
        command.Parameters.AddWithValue("$role", role.ToString().ToLowerInvariant());
    }
}
