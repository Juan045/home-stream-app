using Microsoft.Extensions.Logging;
using StreamMedia.Application.Media;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Application.Streaming;
using StreamMedia.Application.Streaming.Interfaces;
using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Encoder;

// POST /api/v1/media/{id}/encode
// Arranca el mismo pipeline HLS de /stream con el perfil de biblioteca (AV1) y el asset
// pineado. El audio y los subtitulos que el prewarm ya dejo listos no se rehacen.
public sealed class EncodeMediaUseCase(
    IMediaRepository mediaRepository,
    MediaPathResolver mediaPathResolver,
    CapacityGuard capacityGuard,
    AssetBuilder builder,
    IAssetStore store,
    ILogger<EncodeMediaUseCase> log)
{
    public async Task<EncodeStatus> ExecuteAsync(Guid id, CancellationToken ct = default)
    {
        var media = await mediaRepository.GetByIdAsync(id, ct)
            ?? throw new MediaPathException("media_not_found", $"No existe una ficha con id {id}");

        // Misma resolucion y validacion que la rama id_media de /stream.
        var source = mediaPathResolver.Resolve(media.FilePath);
        var assetId = store.AssetIdFor(source);

        var existing = builder.Get(assetId);
        var manifest = existing is null && store.Exists(assetId)
            ? await store.ReadManifestAsync(assetId, ct)
            : null;

        // 409 si el video ya se despacho. En memoria manda el mismo criterio que open
        // (IsVideoDispatched); en disco, el manifest es la unica fuente.
        var dispatched = existing is not null
            ? builder.IsVideoDispatched(existing)
            : manifest is { Video: not ArtifactState.Pending };

        if (dispatched)
        {
            var state = existing?.Video.State ?? manifest?.Video ?? ArtifactState.Pending;
            var codec = existing?.Options.VideoCodec ?? manifest?.Options?.VideoCodec;
            throw new AssetAlreadyEncodedException(
                $"El asset {assetId} ya existe (video: {EncodeStatus.StateName(state)}, codec: {codec}). " +
                $"Para recodificarlo hay que borrar {store.Paths(assetId).Root} primero.");
        }

        // ponytail: este chequeo y el open no son atomicos. Si dos POST llegan a la vez, el
        // segundo encuentra el asset ya creado, open ignora las opciones y devuelve el mismo
        // estado: es idempotente y no lanza dos FFmpeg.
        await capacityGuard.EnsureAsync(source, ct);

        var ignored = media.SourceInfo?.GetIgnoredTracks() ?? IgnoredTracks.None;

        var asset = await builder.OpenAsync(
            source, ignored, media.Strategy, TranscodeProfiles.Archive, pin: true, ct: ct);

        log.LogInformation(
            "Codificacion de biblioteca iniciada {IdMedia} {AssetId} {VideoCodec}",
            id, asset.Id, asset.Options.VideoCodec);

        return EncodeStatus.From(id, asset.Id, asset, manifest: null);
    }
}
