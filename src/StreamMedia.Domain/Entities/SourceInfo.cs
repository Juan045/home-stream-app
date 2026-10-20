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
    IReadOnlyList<SubtitleTrack> SubtitleTracks,
    long? BitRate = null,
    string? VideoProfile = null,
    int? VideoLevel = null,
    string? ColorTransfer = null) // null = ficha vieja, anterior al dato; ffprobe sin dato da "unknown"
{
    public StreamStrategy GetStrategy()
        => VideoCodec == "h264" ? StreamStrategy.Remux : StreamStrategy.Transcode;

    // Metodo y no propiedad: no se serializa a la ficha ni al manifest.
    // PQ (HDR10, o Dolby Vision con base HDR10) o HLG. DV perfil 5 no se tonemapea bien.
    public bool IsHdr() => ColorTransfer is "smpte2084" or "arib-std-b67";

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
    string Title="",
    string? Profile=null
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