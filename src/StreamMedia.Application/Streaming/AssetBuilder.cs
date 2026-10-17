using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.Streaming.Interfaces;
using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Streaming;

/*
    Abre assets y registra que hay que generar.
    TODO: Lanzar FFMPEG
*/
public class AssetBuilder(
    IAssetStore store, 
    IMediaAnalyzer analyzer, 
    IFfmpegRunner ffmpeg,
    TranscodeOptions defaults,
    int maxConcurrent,
    ILogger<AssetBuilder> log) : IAsyncDisposable
{
    readonly ConcurrentDictionary<string, Asset> _assets = new();
    readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    // el semaforo de FFmpeg
    readonly SemaphoreSlim _slots  = new(maxConcurrent, maxConcurrent);
    readonly CancellationTokenSource _shutdown = new();

    // para esperarlas al apagar
    readonly ConcurrentDictionary<Task, byte> _tasks = new();          

    // Idempotente
    public Task<Asset> OpenAsync(string source, IgnoredTracks ignored, StreamStrategy? strategy, CancellationToken ct = default)
        => OpenCoreAsync(source, ignored, strategy, buildVideo: true, ct);

    /// <summary>Encola audio y subtitulos pendientes sin esperar y sin tocar el video.</summary>
    public void QueuePrewarm(string source, IgnoredTracks ignored)
    {
        var task = Task.Run(() => PrewarmAsync(source, ignored));
        _tasks[task] = 0;
        _ = task.ContinueWith(t => _tasks.TryRemove(t, out _), TaskScheduler.Default);
    }

    /// <summary>Encola audio y subtitulos pendientes sin esperar y sin tocar el video.</summary>
    public void QueuePrewarm(string source, IReadOnlySet<int> ignoredAudio, IReadOnlySet<int> ignoredSubtitles)
        => QueuePrewarm(source, new IgnoredTracks(ignoredAudio, ignoredSubtitles));

    private async Task PrewarmAsync(string source, IgnoredTracks ignored)
    {
        try
        {
            await OpenCoreAsync(source, ignored, strategy: null, buildVideo: false, _shutdown.Token);
            log.LogInformation("Prewarm encolado para {Source}", source);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Prewarm fallo para {Source}", source);
        }
    }

    private async Task<Asset> OpenCoreAsync(string source, IgnoredTracks ignored, StreamStrategy? strategy, bool buildVideo, CancellationToken ct)
    {
        var assetId = store.AssetIdFor(source);
        var gate = _locks.GetOrAdd(assetId, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct); // lock no admite await; esto evita dos aperturas simultaneas
        try
        {
            if (!_assets.TryGetValue(assetId, out var asset))
                _assets[assetId] = asset = await LoadAsync(assetId, source, strategy, ct);

            // ponytail: la estrategia pedida se ignora si el asset ya existe (abrir es idempotente).
            // Upgrade: resetear solo el artefacto de video cuando difiera (el audio es independiente).
            if (strategy is { } requested && requested != asset.Strategy)
                log.LogWarning(
                    "Estrategia pedida {Requested} distinta de la del asset {Actual}; se mantiene la del asset {AssetId}",
                    requested, asset.Strategy, assetId);

            // Aca y no en LoadAsync: ese solo corre con el cache frio (I6).
            asset.Info = asset.Info.WithIgnoredTracks(ignored);
            SelectTracks(asset);

            await store.WriteManifestAsync(assetId, ToManifest(asset), ct);
            await store.TouchAsync(assetId, ct);
            StartPendingBuilds(asset, buildVideo);
            return asset;
        }
        finally { gate.Release(); }
    }

    async Task<Asset> LoadAsync(string assetId, string source, StreamStrategy? strategy, CancellationToken ct)
    {
        var cached = await store.ReadManifestAsync(assetId, ct);

        // Con manifest, las opciones salen de el y no de la config del server (I7).
        var asset = cached is null
            ? new Asset(assetId, source, await analyzer.AnalyzeAsync(source, ct)) { Options = defaults }
            : new Asset(assetId, source, cached.Info)
            {
                Options = cached.Options ?? defaults,
                Strategy = cached.Strategy,
                Pinned = cached.Pinned,
                CachedSubtitles = cached.Subtitles,
                CachedFailedSubtitles = new HashSet<int>(cached.SubtitlesFailed),
            };

        // Cache frio: manda lo que pide quien abre; sin eleccion (file_path) queda la del codec.
        if (cached is null && strategy is { } requested)
            asset.Strategy = requested;

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
        Strategy = a.Strategy,
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

    // Orden: video, audio, subtitulos. Cada Spawn llega a WaitAsync de forma sincrona y en este
    // orden, asi que el semaforo los atiende en ese orden (el video es lo unico que el usuario espera).
    void StartPendingBuilds(Asset asset, bool buildVideo = true)
    {
        var paths = store.Paths(asset.Id);

        if (buildVideo && asset.Video.State == ArtifactState.Pending)
            Spawn(asset, "video", asset.Video, paths.Video,
                TranscodeCommandBuilder.Video(asset.Source, paths.Video, asset.Strategy, asset.Options));

        foreach (var (index, artifact) in asset.Audio.Where(a => a.Value.State == ArtifactState.Pending))
            Spawn(asset, $"audio:{index}", artifact, paths.Audio(index),
                TranscodeCommandBuilder.Audio(asset.Source, paths.Audio(index), asset.Info, asset.Options, index));

        foreach (var (index, sub) in asset.Subtitles.Where(s => s.Value.Artifact.State == ArtifactState.Pending))
            Spawn(asset, $"sub:{index}", sub.Artifact, paths.Subs,
                TranscodeCommandBuilder.Subtitle(asset.Source, Path.Combine(paths.Subs, sub.FileName), index));
    }

    void Spawn(Asset asset, string key, Artifact artifact, string outputDir, List<string> args)
    {
        store.PrepareBuild(outputDir);              // antes de marcar Building: si falla, no queda colgado
        artifact.SecondsDone = 0;
        artifact.State = ArtifactState.Building;    // sincrono y bajo el lock: evita doble lanzamiento

        var task = RunAsync(asset, key, artifact, args); // sin Task.Run: ver nota abajo
        _tasks[task] = 0;
        _ = task.ContinueWith(t => _tasks.TryRemove(t, out _), TaskScheduler.Default);
    }

    // Nunca lanza: nadie espera esta tarea, una excepcion se perderia en silencio.
    async Task RunAsync(Asset asset, string key, Artifact artifact, List<string> args)
    {
        var ct = _shutdown.Token;
        try { await _slots.WaitAsync(ct); }
        catch (OperationCanceledException) { artifact.State = ArtifactState.Pending; return; }

        try
        {
            log.LogInformation("Build iniciado {AssetId} {Artifact}", asset.Id, key);
            await ffmpeg.RunAsync(args, seconds => artifact.SecondsDone = seconds, ct);
            artifact.State = ArtifactState.Ready;
            log.LogInformation("Build completo {AssetId} {Artifact}", asset.Id, key);
        }
        catch (OperationCanceledException)
        {
            artifact.State = ArtifactState.Pending; // shutdown: se rehace en la proxima apertura
        }
        catch (Exception ex) // FfmpegException, o Process.Start si falta el binario
        {
            artifact.State = ArtifactState.Failed;
            artifact.Error = ex.Message;
            log.LogError(ex, "Build fallo {AssetId} {Artifact}", asset.Id, key);
        }
        finally
        {
            _slots.Release();
            await SaveAsync(asset);
        }
    }

    // Serializa con el lock del asset: varios artefactos terminan a la vez y sin esto
    // dos WriteManifestAsync pisarian el mismo archivo.
    async Task SaveAsync(Asset asset)
    {
        var gate = _locks.GetOrAdd(asset.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try { await store.WriteManifestAsync(asset.Id, ToManifest(asset), CancellationToken.None); }
        catch (Exception ex) { log.LogError(ex, "No se pudo guardar el manifest {AssetId}", asset.Id); }
        finally { gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync();       // el runner mata cada proceso
        await Task.WhenAll(_tasks.Keys);     // RunAsync no lanza, es seguro
        _shutdown.Dispose();
    }
}