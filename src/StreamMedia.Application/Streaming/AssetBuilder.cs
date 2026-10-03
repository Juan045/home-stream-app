using System.Collections.Concurrent;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.Streaming.Interfaces;
using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Streaming;

/*
    Abre assets y registra que hay que generar.
    TODO: Lanzar FFMPEG
*/
public class AssetBuilder(IAssetStore store, IMediaAnalyzer analyzer, TranscodeOptions defaults)
{
    readonly ConcurrentDictionary<string, Asset> _assets = new();
    readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    // Idempotente
    public async Task<Asset> OpenAsync(string source, IgnoredTracks ignored, CancellationToken ct = default)
    {
        var assetId = store.AssetIdFor(source);
        var gate = _locks.GetOrAdd(assetId, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct); // lock no admite await; esto evita dos aperturas simultaneas
        try
        {
            if (!_assets.TryGetValue(assetId, out var asset))
                _assets[assetId] = asset = await LoadAsync(assetId, source, ct);

            // Aca y no en LoadAsync: ese solo corre con el cache frio (I6).
            asset.Info = asset.Info.WithIgnoredTracks(ignored);
            SelectTracks(asset);

            await store.WriteManifestAsync(assetId, ToManifest(asset), ct);
            await store.TouchAsync(assetId, ct);
            // TODO(ffmpeg): StartPendingBuilds(asset);
            return asset;
        }
        finally { gate.Release(); }
    }

    async Task<Asset> LoadAsync(string assetId, string source, CancellationToken ct)
    {
        var cached = await store.ReadManifestAsync(assetId, ct);

        // Con manifest, las opciones salen de el y no de la config del server (I7).
        var asset = cached is null
            ? new Asset(assetId, source, await analyzer.AnalyzeAsync(source, ct)) { Options = defaults }
            : new Asset(assetId, source, cached.Info)
            {
                Options = cached.Options ?? defaults,
                Pinned = cached.Pinned,
                CachedSubtitles = cached.Subtitles,
                CachedFailedSubtitles = new HashSet<int>(cached.SubtitlesFailed),
            };

        var paths = store.Prepare(assetId);
        asset.Video.State = StateOnDisk(paths.Video);
        return asset;
    }

    // Registrar el artefacto ES la decision: lo que no queda en Audio/Subtitles no se genera ni se declara.
    void SelectTracks(Asset asset)
    {
        var paths = store.Paths(asset.Id);

        var audio = asset.Info.AudioTracks.Where(t => !t.Ignore).Select(t => t.Index).ToHashSet();
        foreach (var index in asset.Audio.Keys.Except(audio))
            asset.Audio.TryRemove(index, out _);
        foreach (var index in audio.Except(asset.Audio.Keys))
            asset.Audio[index] = new Artifact { State = StateOnDisk(paths.Audio(index)) };

        var subs = asset.Info.SubtitleTracks.Where(t => !t.Ignore).ToList();
        foreach (var index in asset.Subtitles.Keys.Except(subs.Select(t => t.Index)))
            asset.Subtitles.TryRemove(index, out _);
        foreach (var track in subs.Where(t => !asset.Subtitles.ContainsKey(t.Index)))
        {
            var sub = new SubtitleArtifact($"sub_{track.Index}_{track.Language}.vtt");
            sub.Artifact.State = SubtitleState(asset, track.Index, Path.Combine(paths.Subs, sub.FileName));
            asset.Subtitles[track.Index] = sub;
        }
    }

    // Ready solo si FFmpeg cerro la playlist: un build interrumpido se rehace desde cero.
    ArtifactState StateOnDisk(string directory)
        => store.IsPlaylistComplete(directory) ? ArtifactState.Ready : ArtifactState.Pending;

    // Un bitmap (PGS/VobSub) que ya fallo no se reintenta en cada apertura.
    ArtifactState SubtitleState(Asset asset, int index, string vtt)
    {
        if (asset.CachedSubtitles.ContainsKey(index) && store.FileExists(vtt))
            return ArtifactState.Ready;
        return asset.CachedFailedSubtitles.Contains(index) ? ArtifactState.Failed : ArtifactState.Pending;
    }

    static AssetManifest ToManifest(Asset a) => new()
    {
        AssetId = a.Id,
        Source = a.Source,
        Duration = a.Info.Duration,
        Strategy = a.Info.GetStrategy(),
        Video = a.Video.State,
        Audio = a.Audio.ToDictionary(x => x.Key, x => x.Value.State),
        Subtitles = a.ReadySubtitles(),
        SubtitlesFailed = a.FailedSubtitlesIndexes(),
        Info = a.Info,
        Options = a.Options,
        Pinned = a.Pinned,
    };

    public Asset? Get(string assetId) => 
        _assets.TryGetValue(assetId, out var asset) ? asset : null;

    // Cuenta artefactos (video, audio y subtitulos) de todos los assets, como el semaforo de FFmpeg.
    public int BuildingCount() => 
        _assets.Values.Sum(a => Artifacts(a).Count(IsBuilding));

    public HashSet<string> BuildingAssetIds()
        => _assets.Values.Where(a => Artifacts(a).Any(IsBuilding)).Select(a => a.Id).ToHashSet();

    // Sin esto, un asset barrido por el GC se seguiria sirviendo con segmentos inexistentes.
    // ponytail: el lock del asset queda en _locks (pesa casi nada), no se quita para no pisar a un waiter.
    public void Forget(string assetId) => _assets.TryRemove(assetId, out _);

    static IEnumerable<Artifact> Artifacts(Asset a)
        => a.Audio.Values.Append(a.Video).Concat(a.Subtitles.Values.Select(s => s.Artifact));

    static bool IsBuilding(Artifact a) => a.State == ArtifactState.Building;
}