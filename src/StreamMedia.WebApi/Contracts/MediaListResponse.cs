using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using MediaEntity = StreamMedia.Domain.Entities.Media;

namespace StreamMedia.WebApi.Contracts;

public record MediaListResponse(
    IReadOnlyList<MediaListItemResponse> Items, 
    int Total, 
    int Limit, 
    int Offset
)
{
    public static MediaListResponse From(IReadOnlyList<MediaListItemResponse> items, int pages, int limit, int offset)
    {
        return new (
            items,
            pages,
            limit,
            offset
        );
    }
}