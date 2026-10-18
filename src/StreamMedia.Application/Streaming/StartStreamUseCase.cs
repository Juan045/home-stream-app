using StreamMedia.Domain.Entities;
using StreamMedia.Application.Media;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.Streaming.Interfaces;

// alias para que no confunda el namespace
using MediaEntity = StreamMedia.Domain.Entities.Media;

namespace StreamMedia.Application.Streaming;

// POST /api/v1/stream
// Paso 0: solo valida la ruta. Todavia no abre nada.
public sealed class StartStreamUseCase(
    IMediaRepository mediaRepository,
    MediaPathResolver mediaPathResolver,
    CapacityGuard capacityGuard,
    AssetBuilder builder,
    SessionManager sessionManager,
    IAssetStore store)
{
    public async Task<StreamStarted> ExecuteAsync(string? filePath, string? IdMedia, CancellationToken ct = default)
    {
        // XOR: exactamente uno. Vacio o solo espacios cuenta como ausente.
        if (string.IsNullOrWhiteSpace(IdMedia) == string.IsNullOrWhiteSpace(filePath))
            throw new InvalidRequestException("Enviar exactamente uno: id_media o file_path.");

        var (source, media) = await ResolveSourceAsync(filePath, IdMedia, ct);

        await capacityGuard.EnsureAsync(source, ct);

        var ignored = media?.SourceInfo?.GetIgnoredTracks() ?? IgnoredTracks.None;

        var asset = await builder.OpenAsync(source, ignored, media?.Strategy, ct: ct);

        var session = sessionManager.Create(asset.Id);

        var playable = asset.IsPlayable(store.CountSegments(store.Paths(asset.Id).Video));

        return new StreamStarted(asset, session, playable);
    }

    private async Task<(string Source, MediaEntity? media)> ResolveSourceAsync(
        string? filePath, string? idMedia, CancellationToken ct)
    {
        // cargar las settings del streaming.
        if (!string.IsNullOrWhiteSpace(filePath))
            return (mediaPathResolver.ResolveAbsolute(filePath), null);

        if (!Guid.TryParse(idMedia, out var mediaId))
            throw new MediaPathException("invalid_media_id", "El identificador del medio no es válido");

        var media = await mediaRepository.GetByIdAsync(mediaId, ct)
            ?? throw new MediaPathException("media_not_found", "El medio no existe");

        return (mediaPathResolver.Resolve(media.FilePath), media);
    }
}