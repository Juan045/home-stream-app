using Microsoft.Extensions.Logging;
using StreamMedia.Application.Cache;
using StreamMedia.Application.Cache.Interfaces;
using StreamMedia.Application.Encoder.Interfaces;
using StreamMedia.Application.Media;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Application.Streaming;
using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Encoder;

// POST /api/v1/media/{id}/encode
// Arranca el mismo pipeline HLS de /stream con el perfil de biblioteca elegido y el asset
// pineado. El audio y los subtitulos que el prewarm ya dejo listos no se rehacen.
public sealed class EncodeMediaUseCase(
    IMediaRepository mediaRepository,
    MediaPathResolver mediaPathResolver,
    CapacityGuard capacityGuard,
    AssetBuilder builder,
    ISourceIdentity identity,
    CacheLayout layout,
    IEncodingSettingsRepository encodingSettings,
    ILogger<EncodeMediaUseCase> log)
{
    public async Task<EncodeStatus> ExecuteAsync(Guid id, CancellationToken ct = default)
    {
        var media = await mediaRepository.GetByIdAsync(id, ct)
            ?? throw new MediaPathException("media_not_found", $"No existe una ficha con id {id}");

        // Misma resolucion y validacion que la rama id_media de /stream.
        var source = mediaPathResolver.Resolve(media.FilePath);
        var assetId = identity.AssetIdFor(source);

        // 409 si el video ya se despacho. Un solo criterio, en memoria o en disco: el mismo de open
        // (IsVideoDispatched). Un video interrumpido sale pending de PeekAsync y se puede relanzar (C3).
        var existing = await builder.PeekAsync(assetId, ct);
        if (existing is not null && builder.IsVideoDispatched(existing))
            throw new AssetAlreadyEncodedException(
                $"El asset {assetId} ya existe (video: {EncodeStatus.StateName(existing.Video.State)}, " +
                $"codec: {existing.Options.VideoCodec}). Para recodificarlo hay que borrar {layout.Paths(assetId).Root} primero.");

        // ponytail: este chequeo y el open no son atomicos. Si dos POST llegan a la vez, el
        // segundo encuentra el asset ya creado, open ignora las opciones y devuelve el mismo
        // estado: es idempotente y no lanza dos FFmpeg.
        await capacityGuard.EnsureAsync(source, ct);

        var ignored = media.SourceInfo?.GetIgnoredTracks() ?? IgnoredTracks.None;

        var settings = await encodingSettings.GetAsync(ct);
        var asset = await builder.OpenAsync(
            source, ignored, media.Strategy, TranscodeProfiles.ForArchive(settings.ArchiveCodec), pin: true, ct: ct);

        log.LogInformation(
            "Codificacion de biblioteca iniciada {IdMedia} {AssetId} {VideoCodec} {TonemapHdr}",
            id, asset.Id, asset.Options.VideoCodec, asset.Options.TonemapHdr);

        return EncodeStatus.From(id, asset.Id, asset);
    }
}
