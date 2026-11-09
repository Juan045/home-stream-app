using StreamMedia.Application.Encoder;
using StreamMedia.Application.Streaming.Interfaces;
using StreamMedia.Domain.DTO;
using StreamMedia.Domain.Entities;
using MediaEntity = StreamMedia.Domain.Entities.Media; // alias: "Media" choca con el namespace

namespace StreamMedia.Application.Artifacts;

/// <summary>
/// Lo que ve el panel de admin de una ficha: el estado de su video y de cada pista que quiere generar.
/// Las pistas salen de la ficha, no del asset: es la que decide cuales se ignoran (I6).
/// </summary>
public sealed record MediaArtifacts(
    Guid IdMedia,
    string? AssetId,
    bool Pinned,
    string? VideoCodec,
    ArtifactStatus Video,
    IReadOnlyList<ArtifactStatus> Audio,
    IReadOnlyList<ArtifactStatus> Subtitles)
{
    /// <summary>Nada generado todavia: sin asset en memoria ni manifest.</summary>
    public static MediaArtifacts Idle(MediaEntity media) => new(
        media.Id,
        media.AssetId,
        Pinned: false,
        VideoCodec: null,
        new ArtifactStatus(null, "idle", 0, false, null),
        [.. WantedAudio(media).Select(PendingAudio)],
        [.. WantedSubtitles(media).Select(PendingSubtitle)]);

    /// <summary>Asset abierto: la memoria es la unica que sabe el avance y que esta en cola.</summary>
    public static MediaArtifacts FromAsset(MediaEntity media, Asset asset)
    {
        var duration = asset.Info.Duration;
        var video = Live(null, asset.Video, TimedProgress(asset.Video, duration));
        var codec = IsDispatched(video.State)
            ? EncodeStatus.ActualVideoCodec(asset.Options, asset.Strategy, asset.Info)
            : null;

        return new(
            media.Id,
            asset.Id,
            asset.Pinned,
            codec,
            video,
            [.. WantedAudio(media).Select(index => asset.Audio.TryGetValue(index, out var audio)
                ? Live(index, audio, TimedProgress(audio, duration))
                : PendingAudio(index))],
            [.. WantedSubtitles(media).Select(index => asset.Subtitles.TryGetValue(index, out var subtitle)
                ? Live(index, subtitle.Artifact, SubtitleProgress(EncodeStatus.StateName(subtitle.Artifact.State)))
                : PendingSubtitle(index))]);
    }

    /// <summary>Asset fuera de memoria: sale del manifest y, si hace falta, del disco.</summary>
    public static MediaArtifacts FromManifest(
        MediaEntity media, string assetId, AssetManifest manifest, IAssetStore store, AssetPaths paths)
    {
        var videoState = ColdArtifactState.Resolve(manifest.Video, () => store.IsPlaylistComplete(paths.Video));
        var codec = IsDispatched(videoState) && manifest.Options is { } options
            ? EncodeStatus.ActualVideoCodec(options, manifest.Strategy, manifest.Info)
            : null;

        return new(
            media.Id,
            assetId,
            manifest.Pinned,
            codec,
            ColdTimed(null, videoState, manifest.VideoError),
            [.. WantedAudio(media).Select(index =>
            {
                ArtifactState? inManifest = manifest.Audio.TryGetValue(index, out var state) ? state : null;
                var audioState = ColdArtifactState.Resolve(inManifest, () => store.IsPlaylistComplete(paths.Audio(index)));
                return ColdTimed(index, audioState, manifest.AudioErrors.GetValueOrDefault(index));
            })],
            [.. WantedSubtitles(media).Select(index =>
            {
                var state = ColdArtifactState.ResolveSubtitle(
                    manifest, index, name => store.FileExists(Path.Combine(paths.Subs, name)));
                var error = state == "failed" ? manifest.SubtitleErrors.GetValueOrDefault(index) : null;
                return new ArtifactStatus(index, state, SubtitleProgress(state), false, error);
            })]);
    }

    static IEnumerable<int> WantedAudio(MediaEntity media)
        => media.SourceInfo!.AudioTracks.Where(t => !t.Ignore).Select(t => t.Index);

    static IEnumerable<int> WantedSubtitles(MediaEntity media)
        => media.SourceInfo!.SubtitleTracks.Where(t => !t.Ignore).Select(t => t.Index);

    static ArtifactStatus PendingAudio(int index) => new(index, "pending", 0, false, null);

    static ArtifactStatus PendingSubtitle(int index) => new(index, "pending", null, false, null);

    static ArtifactStatus Live(int? index, Artifact artifact, double? progress)
        => new(index, EncodeStatus.StateName(artifact.State), progress, artifact.IsQueued, artifact.Error);

    // Fuera de memoria no hay cola ni avance. El error solo cuenta si el estado resuelto es failed.
    static ArtifactStatus ColdTimed(int? index, string state, string? error)
        => new(index, state, state == "ready" ? 1 : 0, false, state == "failed" ? error : null);

    // Video y audio informan por donde va FFmpeg.
    static double TimedProgress(Artifact artifact, double duration)
    {
        if (artifact.State == ArtifactState.Ready)
            return 1;

        if (artifact.State != ArtifactState.Building || duration <= 0)
            return 0;

        return Math.Min(artifact.SecondsDone / duration, 1);
    }

    // El comando de subtitulos no lleva -progress: hasta terminar no hay avance que informar.
    static double? SubtitleProgress(string state) => state == "ready" ? 1 : null;

    // Antes de despachar el video no hay codec: todavia no se eligio, o lo interrumpido no quedo en disco.
    static bool IsDispatched(string state) => state is "building" or "ready" or "failed";
}
