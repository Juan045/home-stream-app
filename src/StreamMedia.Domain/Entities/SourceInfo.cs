using System;

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
}

public record AudioTrack(
    int Index,
    string Language,
    string Codec,
    bool Ignore=false,
    int Channels=0
)
{
    public bool Ignore { get; set; } = Ignore;
}

public record SubtitleTrack(
    int Index,
    string Language,
    bool Ignore=false
)
{
    public bool Ignore { get; set; } = Ignore;
}