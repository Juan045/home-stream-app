using StreamMedia.Application.Encoder.Interfaces;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Encoder;

// PUT /api/v1/settings/encoding
// Aplica a lo que todavia no se genero: no toca assets ni cancela builds.
public sealed class UpdateEncodingSettingsUseCase(IEncodingSettingsRepository repository)
{
    public async Task<EncodingSettings> ExecuteAsync(
        string? liveCodec, string? archiveCodec, CancellationToken ct = default)
    {
        // Antes de guardar: un codec invalido deja el ajuste como estaba.
        var settings = new EncodingSettings(
            Validated(liveCodec, TranscodeProfiles.LiveCodecs, "live_codec"),
            Validated(archiveCodec, TranscodeProfiles.ArchiveCodecs, "archive_codec"));

        await repository.SaveAsync(settings, ct);
        return settings;
    }

    static string Validated(string? codec, IEnumerable<string> available, string field)
        => codec is not null && available.Contains(codec)
            ? codec
            : throw new InvalidRequestException(
                $"{field} invalido: '{codec}' (disponibles: {string.Join(", ", available)})");
}
