using Microsoft.Extensions.Logging;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Application.Streaming;
using StreamMedia.Domain.DTO;
using StreamMedia.Domain.Entities;
using System.Collections.Frozen;

namespace StreamMedia.Application.Media;

public sealed class UpdateMediaUseCase(
    IMediaRepository mediaRepository,
    MediaPathResolver mediaPathResolver,
    AssetBuilder assetBuilder,
    ILogger<UpdateMediaUseCase> logger)
{
    public async Task<UpdateMediaResult> ExecuteAsync(Guid id, UpdateMediaRequest request, CancellationToken ct = default)
    {
        var media = await mediaRepository.GetByIdAsync(id, ct);
        if (media is null)
            return UpdateMediaResult.NotFound();

        // Determinar las pistas de audio y subtitulos a ignorar segun
        // los indices que vienen en la request.
        var info = media.SourceInfo ?? throw new InvalidOperationException("Media no tiene SourceInfo.");

        // La guarda va antes de mutar las pistas: si la estrategia es invalida, la ficha no cambia nada.
        StreamStrategy? strategy = null;
        if (request.Strategy is not null)
        {
            strategy = Enum.Parse<StreamStrategy>(request.Strategy, ignoreCase: true);

            // GetStrategy() devuelve lo que el codec admite: solo H.264 se reempaqueta sin recodificar.
            if (strategy == StreamStrategy.Remux && info.GetStrategy() != StreamStrategy.Remux)
                throw new InvalidRequestException(
                    $"No se puede usar remux con el codec {info.VideoCodec}: solo H.264 se reempaqueta sin recodificar.");
        }

        // AUDIO TRACKS
        if (request.IgnoredAudio is not null)
        {
            for (var index = 0; index < info.AudioTracks.Count; index++)
                info.AudioTracks[index].Ignore = request.IgnoredAudio.Contains(index);
        }

        // SUBTITLE TRACKS
        if (request.IgnoredSubtitles is not null)
        {
            for (var index = 0; index < info.SubtitleTracks.Count; index++)
                info.SubtitleTracks[index].Ignore = request.IgnoredSubtitles.Contains(index);
        }

        // Actualizar los campos que traen valores en la request
        media.Update(
            request.Title, request.Kind, request.InList, request.Year, request.Synopsis,
            info, strategy);

        await mediaRepository.UpdateAsync(media, ct);

        try
        {
            var absolutePath = mediaPathResolver.Resolve(media.FilePath);
            var ignored = media.SourceInfo?.GetIgnoredTracks() ?? IgnoredTracks.None;
            assetBuilder.QueuePrewarm(absolutePath, ignored);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo iniciar el prewarm para {FilePath}", media.FilePath);
        }

        return UpdateMediaResult.Updated(media);
    }
}