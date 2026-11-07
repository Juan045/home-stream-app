using StreamMedia.Application.Images;
using MediaEntity = StreamMedia.Domain.Entities.Media;

namespace StreamMedia.WebApi.Contracts;

public record MediaListItemResponse(
    Guid IdMedia, 
    string Title, 
    string? Kind, 
    int? Year, 
    double Duration,
    string? PosterUrl,
    string? BackdropUrl
)
{
    public static MediaListItemResponse From(MediaEntity m)
    {
        return new(
            m.Id,
            m.Title,
            m.Kind,
            m.Year,
            m.SourceInfo!.Duration,
            MediaImageUrl.For(m.Id, MediaImageRole.Poster, m.PosterUpdatedAt),
            MediaImageUrl.For(m.Id, MediaImageRole.Backdrop, m.BackdropUpdatedAt)
        );
    } 
}