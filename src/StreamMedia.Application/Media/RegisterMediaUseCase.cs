using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.Streaming.Interfaces;

namespace StreamMedia.Application.Media;

// Esta clase es la traducción del endpoint POST /api/v1/media de Python.
// No sabe nada de HTTP: el Controller la va a llamar.
public sealed class RegisterMediaUseCase(
    IMediaRepository mediaRepository,
    IMediaAnalyzer mediaAnalyzer,
    MediaPathResolver mediaPathResolver,
    IAssetStore assetStore
)
{
    public async Task<RegisterMediaResult> ExecuteAsync(string relativePath, CancellationToken ct = default)
    {
        // 1. validar y pasar a absoluta (lanza MediaPathException si no sirve)
        var absolutePath = mediaPathResolver.Resolve(relativePath);

        // 2. chequeo de duplicado ANTES de analizar: ffprobe sobre un montaje de red cuesta segundos
        var existingMedia = await mediaRepository.FindByPathAsync(absolutePath, ct);
        if (existingMedia is not null)
        {
            return RegisterMediaResult.AlreadyExists(existingMedia.Id);
        }

        // 3. ffprobe necesita la absoluta; la ficha guarda la relativa
        var sourceInfo = await mediaAnalyzer.AnalyzeAsync(absolutePath, ct);

        // 4. crear la entidad Media y guardarla en la base de datos
        var assetId = assetStore.AssetIdFor(absolutePath);
        var media = Domain.Entities.Media.FromSource(relativePath, sourceInfo, assetId);
        await mediaRepository.AddAsync(media, ct);

        return RegisterMediaResult.Created(media);
    }
}