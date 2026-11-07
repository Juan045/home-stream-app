using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Encoder.Interfaces;

public interface IEncodingSettingsRepository
{
    // Sin nada guardado devuelve EncodingSettings.Default.
    Task<EncodingSettings> GetAsync(CancellationToken ct = default);

    // No valida los codecs: eso es de Application.
    Task SaveAsync(EncodingSettings settings, CancellationToken ct = default);
}
