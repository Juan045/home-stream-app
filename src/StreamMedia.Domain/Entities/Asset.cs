using System.Collections.Concurrent;

namespace StreamMedia.Domain.Entities;

public enum AssetStatus { Processing, Ready, Failed }

// Un archivo de origen y todo lo que se genero a partir de el.
public sealed class Asset(string id, string source, SourceInfo info)
{
    public string Id { get; } = id;
    public string Source { get; } = source; // ruta absoluta del origen

    // Se reemplaza en cada open (los ignore de la ficha), por eso tiene set.
    public SourceInfo Info { get; set; } = info;

    public bool Pinned { get; set; } // el GC no lo toca (codificacion de biblioteca)

    public Artifact Video { get; } = new();

    // opciones de transcodificacion (codificacion de biblioteca)
    public TranscodeOptions Options { get; set; } = new();

    // Un artefacto por pista registrada (indice de pista -> artefacto).
    public ConcurrentDictionary<int, Artifact> Audio { get; } = new();
    public ConcurrentDictionary<int, SubtitleArtifact> Subtitles { get; } = new();

    // REVISAR
    public IReadOnlyDictionary<int, string> CachedSubtitles { get; init; } = new Dictionary<int, string>();
    public IReadOnlySet<int> CachedFailedSubtitles { get; init; } = new HashSet<int>();

    

    public AssetStatus Status
    {
        get
        {
            if (Video.State == ArtifactState.Failed)
                return AssetStatus.Failed;

            var allReady = Video.State == ArtifactState.Ready
                && Audio.Values.All(a => a.State == ArtifactState.Ready);

            return allReady ? AssetStatus.Ready : AssetStatus.Processing;
        }
    }

    // El error del video, o el de la primera pista de audio que fallo.
    public string? Error
        => !string.IsNullOrEmpty(Video.Error)
            ? Video.Error
            : Audio.Values.Select(a => a.Error).FirstOrDefault(e => !string.IsNullOrEmpty(e));

    // Avance del build de video, entre 0 y 1.
    public double Progress
    {
        get
        {
            if (Info.Duration <= 0) return 0;
            if (Video.State == ArtifactState.Ready) return 1;
            return Math.Min(Video.SecondsDone / Info.Duration, 1);
        }
    }

    /* Quien le pide a IAssetStore tiene que realizar:
        int segmentCount = store.CountSegments(store.Paths(asset.Id).Video);
        bool isPlayable = IsPlayable(asset, segmentCount); 
    */
    public bool IsPlayable(int videoSegmentCount)
    {
        const int minSegments = 2;

        return Video.State == ArtifactState.Ready
            || videoSegmentCount >= minSegments;
    }

    public IEnumerable<Artifact> AllArtifacts()
        => [Video, .. Audio.Values, .. Subtitles.Values.Select(s => s.Artifact)];

    // Solo las pistas efectivamente extraidas (indice -> nombre del .vtt).
    public Dictionary<int, string> ReadySubtitles()
        => Subtitles
            .Where(s => s.Value.Artifact.State == ArtifactState.Ready)
            .ToDictionary(s => s.Key, s => s.Value.FileName);

    public List<int> FailedSubtitlesIndexes()
        => [.. Subtitles.Where(s => s.Value.Artifact.State == ArtifactState.Failed).Select(s => s.Key).Order()];
}