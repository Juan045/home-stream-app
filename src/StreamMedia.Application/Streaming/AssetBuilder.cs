using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StreamMedia.Application.Cache;
using StreamMedia.Application.Cache.Interfaces;
using StreamMedia.Application.Encoder;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Application.Streaming.Interfaces;
using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Streaming;

// Ciclo de vida del asset en memoria: abre, decide que falta, lanza y supervisa los builds y guarda el manifest.
public class AssetBuilder(
    ISourceIdentity identity,
    IAssetRepository manifests,
    ICacheSpace space,
    IHlsOutput hls,
    CacheLayout layout,
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

    // Idempotente. strategy, options y pin solo se aplican si el video todavia no se despacho (I7).
    // El TonemapHdr de options se ignora: lo decide la fuente (asset.Info).
    public Task<Asset> OpenAsync(
        string source, IgnoredTracks ignored, StreamStrategy? strategy,
        TranscodeOptions? options = null, bool pin = false, CancellationToken ct = default)
        => OpenCoreAsync(source, ignored, strategy, buildVideo: true, options: options, pin: pin, ct: ct);

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
            await OpenCoreAsync(source, ignored, strategy: null, buildVideo: false, options: null, pin: false, ct: _shutdown.Token);
            log.LogInformation("Prewarm encolado para {Source}", source);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Prewarm fallo para {Source}", source);
        }
    }

    private async Task<Asset> OpenCoreAsync(
        string source, IgnoredTracks ignored, StreamStrategy? strategy, bool buildVideo,
        TranscodeOptions? options, bool pin, CancellationToken ct)
    {
        var assetId = identity.AssetIdFor(source);
        var gate = _locks.GetOrAdd(assetId, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct); // lock no admite await; esto evita dos aperturas simultaneas
        try
        {
            AssetManifest? stored = null; 
            if (!_assets.TryGetValue(assetId, out var asset))
            {
                stored = await manifests.ReadManifestAsync(assetId, ct);
                _assets[assetId] = asset = await LoadAsync(assetId, source, strategy, stored, ct);
            }

            // Estrategia, opciones y pin de quien abre: solo si el video todavia no se despacho. Con
            // el video ya despachado mandan las del manifest (I7): es lo que hace que "Play despues
            // de encode" no recodifique y anuncie el codec de biblioteca en vez del de en vivo.
            if (!IsVideoDispatched(asset))
            {
                // El prewarm pasa null y no cambia nada; el Play o el encode traen la de la ficha.
                if (strategy is { } requested) asset.Strategy = requested;

                // Un encode interrumpido queda pineado: Play no le cambia el perfil, solo otro /encode.
                var chosen = pin || !asset.Pinned ? options ?? asset.Options : asset.Options;

                // El tonemap no lo elige quien abre: lo decide la fuente. HDR siempre sale SDR, con cualquier codec.
                asset.Options = chosen with { TonemapHdr = asset.Info.IsHdr() };
                if (pin) asset.Pinned = true;
                // TODO: esto reanuda de cero un prewarm o un encode interrumpido (FFmpeg no
                // retoma lo ya codificado). Ver "reanudacion del encode" en el README.
            }
            // ponytail: con el video despachado la estrategia pedida se ignora (abrir es idempotente).
            // Upgrade: resetear solo el artefacto de video cuando difiera (el audio es independiente).
            else if (strategy is { } ignoredStrategy && ignoredStrategy != asset.Strategy)
            {
                log.LogWarning(
                    "Estrategia pedida {Requested} distinta de la del asset {Actual}; se mantiene la del asset {AssetId}",
                    ignoredStrategy, asset.Strategy, assetId);
            }

            // Aca y no en LoadAsync: ese solo corre con el cache frio (I6).
            asset.Info = asset.Info.WithIgnoredTracks(ignored);
            SelectTracks(asset, stored);

            await space.TouchAsync(assetId, ct);
            StartPendingBuilds(asset, buildVideo);

            // Despues de despachar, para que el manifest diga building: si el proceso muere en seco,
            // al volver se reconoce como interrumpido aunque ningun otro build haya escrito (C17).
            // Un build que termina antes espera este lock en SaveAsync y escribe despues.
            // Sin el ct de la request: lo despachado ya corre aunque el cliente se haya ido.
            await manifests.WriteManifestAsync(assetId, ToManifest(asset), CancellationToken.None);
            return asset;
        }
        finally { gate.Release(); }
    }

    async Task<Asset> LoadAsync(
        string assetId, string source, StreamStrategy? strategy, AssetManifest? stored, CancellationToken ct)
    {
        Asset asset;
        if (stored is not null)
            asset = Restore(assetId, source, stored);
        else
        {
            asset = new Asset(assetId, source, await analyzer.AnalyzeAsync(source, ct)) { Options = defaults };

            // Cache frio: manda lo que pide quien abre; sin eleccion (file_path) queda la del codec.
            if (strategy is { } requested)
                asset.Strategy = requested;

            // Sin manifest (borrado, o ilegible) un video completo en disco se aprovecha igual.
            ArtifactRecovery.Recover(asset.Video, inManifest: null, error: null,
                () => hls.IsPlaylistComplete(assetId, OutputFolder.Video));
        }

        hls.CreateDirectories(assetId);
        // Despues de crear: Touch no escribe si no existe la raiz. Sin marcador seria el mas
        // viejo del LRU (0.0) y el primero en irse.
        await space.TouchAsync(assetId, ct);

        // Manifest anterior a ColorTransfer con el video sin construir: sin el dato no se decide el tonemap.
        // Solo con el video pendiente: si ya esta, no se necesita y el origen podria no existir.
        if (asset.Info.ColorTransfer is null && asset.Video.State == ArtifactState.Pending)
            asset.Info = asset.Info with { ColorTransfer = (await analyzer.AnalyzeAsync(source, ct)).ColorTransfer };
        return asset;
    }

    // El asset tal como lo dejo el manifest, con cada artefacto resuelto por ArtifactRecovery. No escribe nada.
    Asset Restore(string assetId, string source, AssetManifest stored)
    {
        // Con manifest, las opciones salen de el y no de la config del server (I7).
        var asset = new Asset(assetId, source, stored.Info)
        {
            Options = stored.Options ?? defaults,
            Strategy = stored.Strategy,
            Pinned = stored.Pinned,
        };
        ArtifactRecovery.Recover(asset.Video, stored.Video, stored.VideoError,
            () => hls.IsPlaylistComplete(assetId, OutputFolder.Video));
        SelectTracks(asset, stored);
        return asset;
    }

    // Registrar el artefacto ES la decision: lo que no queda en Audio/Subtitles no se genera ni se declara.
    // Cada artefacto nuevo nace por ArtifactRecovery. stored es null con el asset ya en memoria: ahi nada
    // "figura" y decide el disco, que es lo que evita que PrepareBuild borre la playlist completa de una
    // pista que se vuelve a seleccionar. Tambien es la unica salida de un failed sin borrar la carpeta (C14).
    void SelectTracks(Asset asset, AssetManifest? stored)
    {
        var audio = asset.Info.AudioTracks.Where(t => !t.Ignore).Select(t => t.Index).ToHashSet();
        foreach (var index in asset.Audio.Keys.Except(audio))
            asset.Audio.TryRemove(index, out _);

        foreach (var index in audio.Except(asset.Audio.Keys))
        {
            var artifact = new Artifact();
            ArtifactState? inManifest = stored is not null && stored.Audio.TryGetValue(index, out var state) ? state : null;
            ArtifactRecovery.Recover(artifact, inManifest, stored?.AudioErrors.GetValueOrDefault(index),
                () => hls.IsPlaylistComplete(asset.Id, OutputFolder.Audio(index)));
            asset.Audio[index] = artifact;
        }

        var subs = asset.Info.SubtitleTracks.Where(t => !t.Ignore).ToList();
        foreach (var index in asset.Subtitles.Keys.Except(subs.Select(t => t.Index)))
            asset.Subtitles.TryRemove(index, out _);
        foreach (var track in subs.Where(t => !asset.Subtitles.ContainsKey(t.Index)))
        {
            var sub = new SubtitleArtifact($"sub_{track.Index}_{track.Language}.vtt");
            // El manifest no guarda el building de un subtitulo: solo si quedo listo (subtitles) o si fallo.
            ArtifactState? inManifest = stored?.SubtitlesFailed.Contains(track.Index) == true ? ArtifactState.Failed : null;
            ArtifactRecovery.Recover(sub.Artifact, inManifest, stored?.SubtitleErrors.GetValueOrDefault(track.Index),
                () => stored?.Subtitles.ContainsKey(track.Index) == true
                      && hls.FindFile(asset.Id, OutputFolder.Subs, sub.FileName) is not null);
            asset.Subtitles[track.Index] = sub;
        }
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
        VideoError = a.Video.Error,
        AudioErrors = a.Audio
            .Where(x => x.Value.Error is not null)
            .ToDictionary(x => x.Key, x => x.Value.Error!),
        SubtitleErrors = a.Subtitles
            .Where(x => x.Value.Artifact.Error is not null)
            .ToDictionary(x => x.Key, x => x.Value.Artifact.Error!),
        Info = a.Info,
        Options = a.Options,
        Pinned = a.Pinned,
    };

    public Asset? Get(string assetId) =>
        _assets.TryGetValue(assetId, out var asset) ? asset : null;

    /// <summary>
    /// El asset sin abrirlo: el de memoria si esta abierto; si no, el que saldria del manifest y el disco.
    /// Solo lee: no lo registra, no lanza FFmpeg, no crea carpetas ni toca el LRU. null si no hay manifest.
    /// </summary>
    public async Task<Asset?> PeekAsync(string assetId, CancellationToken ct = default)
    {
        // Sin el lock del asset: no muta nada. El abierto se lee tal como esta; el reconstruido es una
        // instancia nueva que nadie mas ve.
        if (_assets.TryGetValue(assetId, out var open))
            return open;

        var stored = await manifests.ReadManifestAsync(assetId, ct);
        return stored is null ? null : Restore(assetId, stored.Source, stored);
    }

    // Cuenta artefactos (video, audio y subtitulos) de todos los assets, como el semaforo de FFmpeg.
    public int BuildingCount() => 
        _assets.Values.Sum(a => Artifacts(a).Count(IsBuilding));

    public HashSet<string> BuildingAssetIds()
        => _assets.Values.Where(a => Artifacts(a).Any(IsBuilding)).Select(a => a.Id).ToHashSet();

    // Sin esto, un asset barrido por el GC se seguiria sirviendo con segmentos inexistentes.
    // ponytail: el lock del asset queda en _locks (pesa casi nada), no se quita para no pisar a un waiter.
    public void Forget(string assetId) => _assets.TryRemove(assetId, out _);

    // "Despachado" = el video ya se mando a construir (building/ready/failed). Un pending es
    // "no arrancado" (prewarm, o encode interrumpido) y se puede reabrir con otras opciones.
    // Spawn marca Building de forma sincrona bajo el lock del asset, asi que no hay un pending
    // con tarea viva que distinguir. Criterio unico: lo usan OpenCore y el guard de /encode.
    public bool IsVideoDispatched(Asset asset)
        => asset.Video.State != ArtifactState.Pending;

    static IEnumerable<Artifact> Artifacts(Asset a)
        => a.Audio.Values.Append(a.Video).Concat(a.Subtitles.Values.Select(s => s.Artifact));

    static bool IsBuilding(Artifact a) => a.State == ArtifactState.Building;

    // Orden: video, audio, subtitulos. Cada Spawn llega a WaitAsync de forma sincrona y en este
    // orden, asi que el semaforo los atiende en ese orden (el video es lo unico que el usuario espera).
    void StartPendingBuilds(Asset asset, bool buildVideo = true)
    {
        var paths = layout.Paths(asset.Id);

        if (buildVideo && asset.Video.State == ArtifactState.Pending)
            Spawn(asset, "video", asset.Video, OutputFolder.Video,
                TranscodeCommandBuilder.Video(asset.Source, paths.Video, asset.Strategy, asset.Options));

        foreach (var (index, artifact) in asset.Audio.Where(a => a.Value.State == ArtifactState.Pending))
            Spawn(asset, $"audio:{index}", artifact, OutputFolder.Audio(index),
                TranscodeCommandBuilder.Audio(asset.Source, paths.Audio(index), asset.Info, asset.Options, index));

        foreach (var (index, sub) in asset.Subtitles.Where(s => s.Value.Artifact.State == ArtifactState.Pending))
            Spawn(asset, $"sub:{index}", sub.Artifact, OutputFolder.Subs,
                TranscodeCommandBuilder.Subtitle(asset.Source, Path.Combine(paths.Subs, sub.FileName), index));
    }

    void Spawn(Asset asset, string key, Artifact artifact, OutputFolder folder, List<string> args)
    {
        hls.PrepareBuild(asset.Id, folder);             // antes de marcar Building: si falla, no queda colgado
        artifact.SecondsDone = 0;
        artifact.Interrupted = false;                   // se relanza: el corte anterior ya no aplica
        artifact.Started =     false;                   // antes que Building: nunca se ve "en curso" por un build anterior
        artifact.State =       ArtifactState.Building;  // sincrono y bajo el lock: evita doble lanzamiento

        // Sin Task.Run: asi RunAsync llega a WaitAsync de forma sincrona y en el orden de StartPendingBuilds.
        var task = RunAsync(asset, key, artifact, args);
        _tasks[task] = 0;
        _ = task.ContinueWith(t => _tasks.TryRemove(t, out _), TaskScheduler.Default);
    }

    // Nunca lanza: nadie espera esta tarea, una excepcion se perderia en silencio.
    async Task RunAsync(Asset asset, string key, Artifact artifact, List<string> args)
    {
        var ct = _shutdown.Token;
        try { await _slots.WaitAsync(ct); }
        catch (OperationCanceledException) { artifact.State = ArtifactState.Pending; return; }

        artifact.Started = true;
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
        catch (FfmpegException ex) // FFmpeg corrio y termino mal: queda failed y no se reintenta
        {
            artifact.State = ArtifactState.Failed;
            artifact.Error = ex.Message;
            log.LogError(ex, "Build fallo {AssetId} {Artifact}", asset.Id, key);
        }
        catch (Exception ex) // FFmpeg no llego a correr (Process.Start, falta el binario): se reintenta al reabrir
        {
            artifact.State = ArtifactState.Pending;
            log.LogError(ex, "Build no pudo arrancar {AssetId} {Artifact}", asset.Id, key);
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
        try 
        { 
            await manifests.WriteManifestAsync(asset.Id, ToManifest(asset), CancellationToken.None); 
        }
        catch (Exception ex) 
        { 
            log.LogError(ex, "No se pudo guardar el manifest {AssetId}", asset.Id); 
        }
        finally 
        { 
            gate.Release(); 
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync();       // el runner mata cada proceso
        await Task.WhenAll(_tasks.Keys);     // RunAsync no lanza, es seguro
        _shutdown.Dispose();
    }
}