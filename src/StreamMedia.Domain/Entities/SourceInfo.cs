using System;
using System.Collections.Frozen;

namespace StreamMedia.Domain.Entities;

// Nace inmutable porque nadie debería "editar" lo que dice el archivo real.
public sealed record SourceInfo(
    double Duration,
    string VideoCodec,
    int Width,
    int Height,
    IReadOnlyList<AudioTrack> AudioTracks,
    IReadOnlyList<SubtitleTrack> SubtitleTracks)
{
    public StreamStrategy GetStrategy()
        => VideoCodec == "h264" ? StreamStrategy.Remux : StreamStrategy.Transcode;

    // Retorna indices que la ficha marco para no generar.
    public IgnoredTracks GetIgnoredTracks() => new(
        AudioTracks.Where(t => t.Ignore).Select(t => t.Index).ToFrozenSet(),
        SubtitleTracks.Where(t => t.Ignore).Select(t => t.Index).ToFrozenSet());
    
    public SourceInfo WithIgnoredTracks(IgnoredTracks ignored) => this with
    {
        AudioTracks = [.. AudioTracks.Select(t => t with { Ignore = ignored.Audio.Contains(t.Index) })],
        SubtitleTracks = [.. SubtitleTracks.Select(t => t with { Ignore = ignored.Subtitles.Contains(t.Index) })],
    };
}

public record AudioTrack(
    int Index,
    string Language,
    string Codec,
    bool Ignore=false,
    int Channels=0,
    string Title=""
)
{
    public bool Ignore { get; set; } = Ignore;
}

public record SubtitleTrack(
    int Index,
    string Language,
    string Codec="unknown",
    bool Ignore=false,
    string Title=""
)
{
    public bool Ignore { get; set; } = Ignore;
}

public readonly record struct IgnoredTracks(
    IReadOnlySet<int> Audio, 
    IReadOnlySet<int> Subtitles)
{
    public static readonly IgnoredTracks None = new(FrozenSet<int>.Empty, FrozenSet<int>.Empty);
}