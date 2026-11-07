using StreamMedia.Application.Encoder.Interfaces;
using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Encoder;

// GET /api/v1/settings/encoding
public sealed class GetEncodingSettingsUseCase(IEncodingSettingsRepository repository)
{
    public Task<EncodingSettings> ExecuteAsync(CancellationToken ct = default) => repository.GetAsync(ct);
}
