using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Streaming;

// Lo unico que sobrevive a un reinicio. Mismas claves que el manifest.json del prototipo.
public sealed record AssetManifest
{
    public string AssetId { get; init; } = "";
    public string Source { get; init; } = "";
    public double Duration { get; init; }
    public StreamStrategy Strategy { get; init; }
    public ArtifactState Video { get; init; }
    public Dictionary<int, ArtifactState> Audio { get; init; } = [];
    public Dictionary<int, string> Subtitles { get; init; } = [];
    public List<int> SubtitlesFailed { get; init; } = [];

    // required: si el JSON no trae "info", deserializar lanza JsonException
    // y el store lo trata como manifest ilegible.
    public required SourceInfo Info { get; init; }

    // null = manifest anterior a este bloque: se cae en la config del server (I7).
    public TranscodeOptions? Options { get; init; }
    public bool Pinned { get; init; }
}