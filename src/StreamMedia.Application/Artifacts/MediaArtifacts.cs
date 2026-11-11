using StreamMedia.Application.Encoder;
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

    /// <summary>
    /// El asset de AssetBuilder.PeekAsync: el abierto, que sabe el avance y que esta en cola, o el
    /// reconstruido del manifest, sin avance ni cola. Los dos con la misma regla que abrirlo.
    /// </summary>
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

    static IEnumerable<int> WantedAudio(MediaEntity media)
        => media.SourceInfo!.AudioTracks.Where(t => !t.Ignore).Select(t => t.Index);

    static IEnumerable<int> WantedSubtitles(MediaEntity media)
        => media.SourceInfo!.SubtitleTracks.Where(t => !t.Ignore).Select(t => t.Index);

    static ArtifactStatus PendingAudio(int index) => new(index, "pending", 0, false, null);

    static ArtifactStatus PendingSubtitle(int index) => new(index, "pending", null, false, null);

    static ArtifactStatus Live(int? index, Artifact artifact, double? progress)
        => new(index, StateName(artifact), progress, artifact.IsQueued, artifact.Error);

    // Interrupted es un Pending cuyo build anterior se corto: solo el admin lo distingue (C11).
    static string StateName(Artifact artifact)
        => artifact.Interrupted ? "interrupted" : EncodeStatus.StateName(artifact.State);

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
