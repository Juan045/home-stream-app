using StreamMedia.Application.Media.Interfaces;

namespace StreamMedia.Application.Media;

// Esta clase es la traducción del endpoint POST /api/v1/media de Python.
// No sabe nada de HTTP: el Controller la va a llamar.

public sealed class RegisterMediaUseCase(
    IMediaRepository mediaRepository,
    IMediaAnalyzer mediaAnalyzer
)
{
    public async Task<RegisterMediaResult> ExecuteAsync(string relativePath, CancellationToken ct = default)
    {
        // 1. chequeo de duplicado ANTES de analizar (igual que en Python, y por la misma razón:
        //    ffprobe sobre un archivo de red cuesta segundos, no vale la pena pagarlos dos veces)
        var existingMedia = await mediaRepository.FindByPathAsync(relativePath, ct);
        if (existingMedia is not null)
        {
            return RegisterMediaResult.AlreadyExists(existingMedia.Id);
        }

        // 2. analizar con ffprobe (acá todavía no existe la implementación real,
        //    pero el use case ya puede compilar y testearse con un fake de IMediaAnalyzer)
        // TODO: revisar relativePath, talvez cambiar a absolute
        var sourceInfo = await mediaAnalyzer.AnalyzeAsync(relativePath, ct);

        // 3. crear la entidad Media y guardarla en la base de datos
        var media = Domain.Entities.Media.FromSource(relativePath, sourceInfo);
        await mediaRepository.AddAsync(media, ct);

        return RegisterMediaResult.Created(media);
    }
}