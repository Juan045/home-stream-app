using StreamMedia.Application.Cache.Interfaces;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Application.Streaming;

namespace StreamMedia.Application.Artifacts;

// GET /api/v1/admin/media/{id}/artifacts
// Solo lee: nunca abre el asset (lanzaria FFmpeg) ni lo marca como usado (alteraria el LRU).
// Usa el asset_id guardado en la ficha, sin stat del origen, igual que GET /encode (TODO #12).
public sealed class GetMediaArtifactsUseCase(
    IMediaRepository mediaRepository,
    AssetBuilder builder,
    IAssetRepository manifests,
    IHlsOutput hls)
{
    public async Task<MediaArtifacts> ExecuteAsync(Guid id, CancellationToken ct = default)
    {
        var media = await mediaRepository.GetByIdAsync(id, ct)
            ?? throw new MediaPathException("media_not_found", $"No existe una ficha con id {id}");

        if (media.AssetId is not { } assetId)
            return MediaArtifacts.Idle(media);

        if (builder.Get(assetId) is { } asset)
            return MediaArtifacts.FromAsset(media, asset);

        var manifest = await manifests.ReadManifestAsync(assetId, ct);

        return manifest is null
            ? MediaArtifacts.Idle(media)
            : MediaArtifacts.FromManifest(media, assetId, manifest, hls);
    }
}
