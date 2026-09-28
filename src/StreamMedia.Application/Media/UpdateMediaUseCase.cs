using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Domain.DTO;

namespace StreamMedia.Application.Media;

public sealed class UpdateMediaUseCase(IMediaRepository mediaRepository)
{
    public async Task<UpdateMediaResult> ExecuteAsync(Guid id, UpdateMediaDTO request, CancellationToken ct = default)
    {
        var media = await mediaRepository.GetByIdAsync(id, ct);
        if (media is null)
            return UpdateMediaResult.NotFound();

        // Determinar las pistas de audio y subtitulos a ignorar segun
        // los indices que vienen en la request.
        var info = media.SourceInfo ?? throw new InvalidOperationException("Media no tiene SourceInfo.");

        // AUDIO TRACKS
        if (request.ignored_audio is not null)
        {
            for (var index = 0; index < info.AudioTracks.Count; index++)
                info.AudioTracks[index].Ignore = request.ignored_audio.Contains(index);
        }

        // SUBTITLE TRACKS
        if (request.ignored_subtitles is not null)
        {
            for (var index = 0; index < info.SubtitleTracks.Count; index++)
                info.SubtitleTracks[index].Ignore = request.ignored_subtitles.Contains(index);
        }

        // Actualizar los campos que traen valores en la request
        media.Update(
            title: request.title,
            kind: request.kind,
            synopsis: request.synopsis,
            info: info);
        
 /*        var info = SourceInfo!;
        SourceInfo = info with
        {
            AudioTracks = ignoredAudio is null
                ? info.AudioTracks
                : info.AudioTracks.Select((track, index) =>
                    track with { Ignore = ignoredAudio.Contains(index) }).ToArray(),
            SubtitleTracks = ignoredSubtitles is null
                ? info.SubtitleTracks
                : info.SubtitleTracks.Select((track, index) =>
                    track with { Ignore = ignoredSubtitles.Contains(index) }).ToArray()
        }; */

        await mediaRepository.UpdateAsync(media, ct);
        return UpdateMediaResult.Updated(media);
    }
}