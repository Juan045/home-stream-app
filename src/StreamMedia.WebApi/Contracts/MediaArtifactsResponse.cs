using StreamMedia.Application.Artifacts;

namespace StreamMedia.WebApi.Contracts;

/// <summary>Lo que dibuja el dialogo "Ver estado" del panel de admin.</summary>
public sealed record MediaArtifactsResponse(
    Guid IdMedia,
    string? AssetId,
    bool Pinned,
    string? VideoCodec,
    ArtifactStatusResponse Video,
    IReadOnlyList<ArtifactStatusResponse> Audio,
    IReadOnlyList<ArtifactStatusResponse> Subtitles)
{
    public static MediaArtifactsResponse From(MediaArtifacts artifacts) => new(
        artifacts.IdMedia,
        artifacts.AssetId,
        artifacts.Pinned,
        artifacts.VideoCodec,
        ArtifactStatusResponse.From(artifacts.Video),
        [.. artifacts.Audio.Select(ArtifactStatusResponse.From)],
        [.. artifacts.Subtitles.Select(ArtifactStatusResponse.From)]);
}
