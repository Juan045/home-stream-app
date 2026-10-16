using StreamMedia.Domain.Entities;

namespace StreamMedia.WebApi.Contracts;

public sealed record StreamResponse(
    string SessionId, string AssetId, string Status, bool Playable, string MasterUrl,
    double DurationSeconds, double Progress, string Strategy,
    IReadOnlyList<AudioTrackDto> AudioTracks, IReadOnlyList<SubtitleTrackDto> SubtitleTracks,
    string? Error)
{
    // Las listas salen de lo REGISTRADO: una pista ignorada no se genera ni se declara.
    public static StreamResponse From(Asset asset, string sessionId, bool playable) => new(
        sessionId, asset.Id,
        asset.Status.ToString().ToLowerInvariant(), // "processing" | "ready" | "failed"
        playable,
        $"/hls/{asset.Id}/master.m3u8",
        asset.Info.Duration, asset.Progress,
        asset.Strategy.ToString().ToLowerInvariant(),
        [.. asset.Info.AudioTracks.Where(t => asset.Audio.ContainsKey(t.Index))
            .Select(t => new AudioTrackDto(t.Index, t.Codec, t.Channels, t.Language, t.Title))],
        [.. asset.Info.SubtitleTracks.Where(t => asset.Subtitles.ContainsKey(t.Index))
            .Select(t => new SubtitleTrackDto(t.Index, t.Codec, t.Language, t.Title, SubtitleUrl(asset, t.Index)))],
        asset.Error);

    // null si todavia no se extrajo o fallo.
    static string? SubtitleUrl(Asset asset, int index)
        => asset.ReadySubtitles().TryGetValue(index, out var name) ? $"/hls/{asset.Id}/subs/{name}" : null;
}

public sealed record AudioTrackDto(int Index, string Codec, int Channels, string Language, string Title);
public sealed record SubtitleTrackDto(int Index, string Codec, string Language, string Title, string? Url);