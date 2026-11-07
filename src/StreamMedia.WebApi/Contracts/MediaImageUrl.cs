using StreamMedia.Application.Images;

namespace StreamMedia.WebApi.Contracts;

// ?v= cambia con cada subida: la URL versionada se puede cachear para siempre.
public static class MediaImageUrl
{
    public static string For(Guid idMedia, MediaImageRole role, DateTimeOffset updatedAt)
        => $"/api/v1/media/{idMedia}/images/{role.ToString().ToLowerInvariant()}?v={updatedAt.ToUnixTimeMilliseconds()}";

    // Para las fichas: sin version no hay imagen, y el campo sale en null en el JSON.
    public static string? For(Guid idMedia, MediaImageRole role, DateTimeOffset? updatedAt)
        => updatedAt is { } version ? For(idMedia, role, version) : null;
}
