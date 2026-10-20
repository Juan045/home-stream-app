using StreamMedia.Application.Media;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Application.Streaming;
using StreamMedia.Application.Streaming.Interfaces;

namespace StreamMedia.Application.Encoder;

// GET /api/v1/media/{id}/encode
// Polling del estado de la codificacion de biblioteca. Usa el asset_id guardado en la ficha
// y NO re-resuelve la ruta ni hace stat del origen: el polling puede durar horas.
public sealed class GetEncodeStatusUseCase(
    IMediaRepository mediaRepository,
    AssetBuilder builder,
    IAssetStore store)
{
    public async Task<EncodeStatus> ExecuteAsync(Guid id, CancellationToken ct = default)
    {
        var media = await mediaRepository.GetByIdAsync(id, ct)
            ?? throw new MediaPathException("media_not_found", $"No existe una ficha con id {id}");

        var assetId = media.AssetId;
        if (assetId is null)
            return EncodeStatus.Idle(id, null);

        // Memoria primero (sabe el progreso); el manifest solo si el asset no esta abierto.
        var asset = builder.Get(assetId);
        var manifest = asset is null ? await store.ReadManifestAsync(assetId, ct) : null;

        return EncodeStatus.From(id, assetId, asset, manifest);
    }
}
