using System;

namespace StreamMedia.Domain.Entities;

// Nace inmutable porque nadie debería "editar" lo que dice el archivo real.
public sealed record SourceInfo(
    double Duration,
    string VideoCodec,
    int Width,
    int Height,
    IReadOnlyList<AudioTrack> AudioTracks,
    IReadOnlyList<SubtitleTrack> SubtitleTracks
);

public sealed record AudioTrack(
    int Index,
    string Language,
    string Codec,
    bool Ignore=false
);

public sealed record SubtitleTrack(
    int Index,
    string Language,
    bool Ignore=false
);