using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Application.Streaming;

namespace StreamMedia.Application.Encoder;

// GET /api/v1/media/{id}/encode
// Polling del estado de la codificacion de biblioteca. Usa el asset_id guardado en la ficha
// y NO re-resuelve la ruta ni hace stat del origen: el polling puede durar horas.
public sealed class GetEncodeStatusUseCase(
    IMediaRepository mediaRepository,
    AssetBuilder builder)
{
    public async Task<EncodeStatus> ExecuteAsync(Guid id, CancellationToken ct = default)
    {
        var media = await mediaRepository.GetByIdAsync(id, ct)
            ?? throw new MediaPathException("media_not_found", $"No existe una ficha con id {id}");

        var assetId = media.AssetId;
        if (assetId is null)
            return EncodeStatus.Idle(id, null);

        // Memoria si esta abierto (sabe el progreso); si no, el manifest leido con la regla de abrir.
        return EncodeStatus.From(id, assetId, await builder.PeekAsync(assetId, ct));
    }
}
