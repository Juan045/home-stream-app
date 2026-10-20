using MediaEntity = StreamMedia.Domain.Entities.Media;

namespace StreamMedia.WebApi.Contracts;

public record MediaListItemResponse(
    Guid IdMedia, 
    string Title, 
    string? Kind, 
    int? Year, 
    double Duration
)
{
    public static MediaListItemResponse From(MediaEntity m)
    {
        return new(
            m.Id,
            m.Title,
            m.Kind,
            m.Year,
            0
        );
    } 
}