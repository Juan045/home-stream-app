using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Domain.DTO;
using StreamMedia.Domain.Entities;
using System.Collections.Frozen;

namespace StreamMedia.Application.Media;

public sealed class UpdateMediaUseCase(IMediaRepository mediaRepository)
{
    public async Task<UpdateMediaResult> ExecuteAsync(Guid id, UpdateMediaRequest request, CancellationToken ct = default)
    {
        var media = await mediaRepository.GetByIdAsync(id, ct);
        if (media is null)
            return UpdateMediaResult.NotFound();

        // Determinar las pistas de audio y subtitulos a ignorar segun
        // los indices que vienen en la request.
        var info = media.SourceInfo ?? throw new InvalidOperationException("Media no tiene SourceInfo.");

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
            info);

        await mediaRepository.UpdateAsync(media, ct);
        return UpdateMediaResult.Updated(media);
    }
}