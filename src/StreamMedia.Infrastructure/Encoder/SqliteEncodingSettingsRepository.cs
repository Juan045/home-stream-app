using StreamMedia.Application.Encoder.Interfaces;
using StreamMedia.Domain.Entities;
using StreamMedia.Infrastructure.Database;

namespace StreamMedia.Infrastructure.Encoder;

public sealed class SqliteEncodingSettingsRepository(SqliteConnectionFactory connectionFactory)
    : IEncodingSettingsRepository
{
    public async Task<EncodingSettings> GetAsync(CancellationToken ct = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(ct);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT live_codec, archive_codec FROM encoding_settings WHERE id = 1";

        using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new EncodingSettings(reader.GetString(0), reader.GetString(1))
            : EncodingSettings.Default;
    }

    public async Task SaveAsync(EncodingSettings settings, CancellationToken ct = default)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(ct);
        using var command = connection.CreateCommand();

        // Un solo upsert: los dos codecs cambian juntos o no cambia ninguno.
        command.CommandText = """
            INSERT INTO encoding_settings (id, live_codec, archive_codec)
            VALUES (1, $liveCodec, $archiveCodec)
            ON CONFLICT(id) DO UPDATE SET
                live_codec = excluded.live_codec,
                archive_codec = excluded.archive_codec
            """;
        command.Parameters.AddWithValue("$liveCodec", settings.LiveCodec);
        command.Parameters.AddWithValue("$archiveCodec", settings.ArchiveCodec);

        await command.ExecuteNonQueryAsync(ct);
    }
}
