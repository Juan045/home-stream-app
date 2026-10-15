using StreamMedia.Domain.Entities;
using MediaEntity = StreamMedia.Domain.Entities.Media; // alias: "Media" choca con el namespace

namespace StreamMedia.WebApi.Contracts; // ajustalo al namespace real de StreamResponse

// TODO: la ficha todavia no persiste genres ni notes. Hoy Genres sale [] y
// Notes null para que el frontend (draftFrom hace genres.join) no rompa.
// Cuando la entidad los tenga: sumarlos a Media, Rehydrate y UpdateMediaRequest,
// y reemplazar los valores fijos de aca por los reales.

/// <summary>Ficha completa; los datos del archivo salen aplanados, sin exponer <c>SourceInfo</c>.</summary>
public record MediaResponse(
    Guid IdMedia,
    string FilePath,
    string FileName,
    string? AssetId,
    string Title,
    string Kind,
    int? Year,
    string? Synopsis,
    IReadOnlyList<string> Genres,
    string? Notes,
    bool InList,
    double Duration,
    string VideoCodec,
    int Width,
    int Height,
    string Strategy,
    IReadOnlyList<AudioTrack> AudioTracks,
    IReadOnlyList<SubtitleTrack> SubtitleTracks,
    DateTime? CreatedAt,
    DateTime? UpdatedAt)
{
    public static MediaResponse From(MediaEntity m)
    {
        var info = m.SourceInfo!; // ver TODO de Media.cs: nunca queda en null

        return new(
            m.Id,
            m.FilePath,
            Path.GetFileName(m.FilePath),
            m.AssetId,
            m.Title,
            m.Kind ?? "film",
            m.Year,
            m.Synopsis,
            [],    // Genres: la entidad todavia no los tiene; el frontend hace .join() y exige un array
            null,  // Notes: idem
            m.InList ?? false,
            info.Duration,
            info.VideoCodec,
            info.Width,
            info.Height,
            info.GetStrategy().ToString().ToLowerInvariant(),
            info.AudioTracks,
            info.SubtitleTracks,
            m.Created_at,
            m.Updated_at);
    }
}