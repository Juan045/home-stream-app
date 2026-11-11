using Microsoft.Extensions.Logging.Abstractions;
using StreamMedia.Application.Cache;
using StreamMedia.Application.Cache.Interfaces;
using StreamMedia.Application.Encoder;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.Streaming;
using StreamMedia.Application.Streaming.Interfaces;
using StreamMedia.Domain.Entities;

public class AssetBuilderPeekTests
{
    const string AssetId = "asset";
    const string Source = "/media/pelicula.mkv";
    const string VideoMap = "0:v:0";
    const string VttName = "sub_0_spa.vtt";

    static readonly SourceInfo Info = new(
        100, "hevc", 1920, 1080,
        [new AudioTrack(0, "spa", "aac")],
        [new SubtitleTrack(0, "spa", "subrip")],
        ColorTransfer: "bt709");

    static readonly TranscodeOptions Live = TranscodeProfiles.ForLive("h264");
    static readonly CacheLayout Layout = new("/cache");

    static AssetManifest ManifestOf(
        ArtifactState video = ArtifactState.Pending,
        Dictionary<int, ArtifactState>? audio = null,
        Dictionary<int, string>? audioErrors = null,
        Dictionary<int, string>? subtitles = null,
        List<int>? subtitlesFailed = null,
        Dictionary<int, string>? subtitleErrors = null,
        TranscodeOptions? options = null,
        StreamStrategy strategy = StreamStrategy.Transcode,
        bool pinned = false) => new()
    {
        AssetId = AssetId,
        Source = Source,
        Info = Info,
        Options = options ?? Live,
        Strategy = strategy,
        Pinned = pinned,
        Video = video,
        Audio = audio ?? [],
        AudioErrors = audioErrors ?? [],
        Subtitles = subtitles ?? [],
        SubtitlesFailed = subtitlesFailed ?? [],
        SubtitleErrors = subtitleErrors ?? [],
    };

    static AssetBuilder BuilderOf(FakeStore store, FakeFfmpeg? ffmpeg = null) => new(
        store, store, store, store, Layout, new FakeAnalyzer(), ffmpeg ?? new FakeFfmpeg(), new TranscodeOptions(), 8,
        NullLogger<AssetBuilder>.Instance);

    [Fact]
    public async Task Sin_manifest_es_null()
    {
        await using var builder = BuilderOf(new FakeStore());

        Assert.Null(await builder.PeekAsync(AssetId));
    }

    // Mirar no es abrir: ni FFmpeg, ni carpetas, ni LRU, ni manifest, ni queda registrado.
    [Fact]
    public async Task Fuera_de_memoria_solo_lee()
    {
        var store = new FakeStore { Manifest = ManifestOf(video: ArtifactState.Building) };
        var ffmpeg = new FakeFfmpeg();
        await using var builder = BuilderOf(store, ffmpeg);

        Assert.NotNull(await builder.PeekAsync(AssetId));

        Assert.Equal(0, store.Writes);
        Assert.Equal(0, store.Touches);
        Assert.Equal(0, store.DirectoryCreations);
        Assert.Equal(0, store.Prepares);
        Assert.Empty(ffmpeg.Calls);
        Assert.Null(builder.Get(AssetId));
    }

    // Abierto, la memoria es la unica que sabe el avance: se devuelve ese y no una copia.
    [Fact]
    public async Task Con_el_asset_abierto_devuelve_la_misma_instancia()
    {
        await using var builder = BuilderOf(new FakeStore());
        await builder.OpenAsync(Source, IgnoredTracks.None, StreamStrategy.Transcode, Live);

        Assert.Same(builder.Get(AssetId), await builder.PeekAsync(AssetId));
    }

    // La playlist que cuenta es la del video: si mirara otra carpeta, nunca llegaria a ready.
    [Theory]
    [InlineData(false, ArtifactState.Pending, true)]
    [InlineData(true, ArtifactState.Ready, false)]
    public async Task Video_en_building_lo_resuelve_su_playlist(bool complete, ArtifactState expected, bool interrupted)
    {
        var store = new FakeStore { Manifest = ManifestOf(video: ArtifactState.Building) };
        if (complete) store.Complete.Add(OutputFolder.Video);
        await using var builder = BuilderOf(store);

        var video = (await builder.PeekAsync(AssetId))!.Video;

        Assert.Equal(expected, video.State);
        Assert.Equal(interrupted, video.Interrupted);
    }

    // Un ready cuyos segmentos ya no estan se rehace: no es un corte, no queda interrumpido.
    [Fact]
    public async Task Video_ready_sin_playlist_cerrada_esta_pendiente()
    {
        await using var builder = BuilderOf(new FakeStore { Manifest = ManifestOf(video: ArtifactState.Ready) });

        var video = (await builder.PeekAsync(AssetId))!.Video;

        Assert.Equal(ArtifactState.Pending, video.State);
        Assert.False(video.Interrupted);
    }

    // Decision del 2026-11-10: con la playlist cerrada y todo, un failed no se reproduce.
    [Fact]
    public async Task Audio_fallido_sigue_fallido_con_su_motivo_sin_mirar_el_disco()
    {
        var store = new FakeStore
        {
            Manifest = ManifestOf(
                audio: new() { [0] = ArtifactState.Failed },
                audioErrors: new() { [0] = "stderr" }),
        };
        store.Complete.Add(OutputFolder.Audio(0));
        await using var builder = BuilderOf(store);

        var audio = (await builder.PeekAsync(AssetId))!.Audio[0];

        Assert.Equal(ArtifactState.Failed, audio.State);
        Assert.Equal("stderr", audio.Error);
        Assert.DoesNotContain(OutputFolder.Audio(0), store.PlaylistChecks);
    }

    [Fact]
    public async Task Audio_que_no_figura_lo_resuelve_la_playlist_de_su_pista()
    {
        var store = new FakeStore { Manifest = ManifestOf() };
        store.Complete.Add(OutputFolder.Audio(0));
        await using var builder = BuilderOf(store);

        var asset = (await builder.PeekAsync(AssetId))!;

        Assert.Equal(ArtifactState.Ready, asset.Audio[0].State);
        Assert.Equal(ArtifactState.Pending, asset.Video.State); // la playlist del audio no cuenta para el video
    }

    // Completo, para un subtitulo, es figurar en el manifest y que su .vtt exista.
    [Theory]
    [InlineData(true, true, ArtifactState.Ready)]
    [InlineData(true, false, ArtifactState.Pending)]
    [InlineData(false, true, ArtifactState.Pending)]
    public async Task Subtitulo_listo_si_figura_y_su_archivo_existe(bool listed, bool onDisk, ArtifactState expected)
    {
        var store = new FakeStore
        {
            Manifest = ManifestOf(subtitles: listed ? new Dictionary<int, string> { [0] = VttName } : null),
        };
        if (onDisk) store.Files.Add((OutputFolder.Subs, VttName));
        await using var builder = BuilderOf(store);

        Assert.Equal(expected, (await builder.PeekAsync(AssetId))!.Subtitles[0].Artifact.State);
    }

    // Aunque figure listo y su archivo exista: si el manifest dice que fallo, fallo.
    [Fact]
    public async Task Subtitulo_fallido_sigue_fallido_con_su_motivo()
    {
        var store = new FakeStore
        {
            Manifest = ManifestOf(
                subtitles: new() { [0] = VttName },
                subtitlesFailed: [0],
                subtitleErrors: new() { [0] = "bitmap" }),
        };
        store.Files.Add((OutputFolder.Subs, VttName));
        await using var builder = BuilderOf(store);

        var subtitle = (await builder.PeekAsync(AssetId))!.Subtitles[0].Artifact;

        Assert.Equal(ArtifactState.Failed, subtitle.State);
        Assert.Equal("bitmap", subtitle.Error);
    }

    // Lo que dice el manifest describe lo que hay en disco, no el ajuste actual (I7).
    [Fact]
    public async Task Opciones_estrategia_y_pin_salen_del_manifest()
    {
        var store = new FakeStore
        {
            Manifest = ManifestOf(
                options: TranscodeProfiles.ForArchive("av1"), strategy: StreamStrategy.Remux, pinned: true),
        };
        await using var builder = BuilderOf(store);

        var asset = (await builder.PeekAsync(AssetId))!;

        Assert.Equal("av1", asset.Options.VideoCodec);
        Assert.Equal(StreamStrategy.Remux, asset.Strategy);
        Assert.True(asset.Pinned);
    }

    // C17, el encode despues del prewarm: audio y subtitulo ya estan, solo corre el video y ningun otro
    // build escribe el manifest mientras dura. Tras un corte en seco queda lo ultimo que se escribio.
    [Fact]
    public async Task Un_corte_en_seco_con_solo_el_video_en_curso_vuelve_interrumpido()
    {
        var store = new FakeStore
        {
            Manifest = ManifestOf(audio: new() { [0] = ArtifactState.Ready }, subtitles: new() { [0] = VttName }),
        };
        store.Complete.Add(OutputFolder.Audio(0));
        store.Files.Add((OutputFolder.Subs, VttName));
        var ffmpeg = new FakeFfmpeg();
        await using var running = BuilderOf(store, ffmpeg);

        await running.OpenAsync(
            Source, IgnoredTracks.None, StreamStrategy.Transcode, TranscodeProfiles.ForArchive("av1"), pin: true);

        Assert.Contains(VideoMap, Assert.Single(ffmpeg.Calls)); // solo corre el video
        Assert.Equal(ArtifactState.Building, store.Manifest!.Video);

        // Otro proceso arranca con ese manifest, sin apagado ordenado de por medio.
        await using var restarted = BuilderOf(new FakeStore { Manifest = store.Manifest });
        var video = (await restarted.PeekAsync(AssetId))!.Video;

        Assert.Equal(ArtifactState.Pending, video.State);
        Assert.True(video.Interrupted);
    }

    sealed class FakeAnalyzer : IMediaAnalyzer
    {
        public Task<SourceInfo> AnalyzeAsync(string absolutePath, CancellationToken ct = default)
            => Task.FromResult(Info);
    }

    // Se queda corriendo hasta que el builder se apaga, como un FFmpeg de verdad.
    sealed class FakeFfmpeg : IFfmpegRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public async Task RunAsync(
            IReadOnlyList<string> args, Action<double>? onProgress = null, CancellationToken ct = default)
        {
            lock (Calls) Calls.Add(args);
            await Task.Delay(Timeout.Infinite, ct);
        }
    }

    sealed class FakeStore : IAssetRepository, ISourceIdentity, IHlsOutput, ICacheSpace
    {
        public AssetManifest? Manifest { get; set; }
        public HashSet<OutputFolder> Complete { get; } = [];
        public HashSet<(OutputFolder Folder, string FileName)> Files { get; } = [];
        public List<OutputFolder> PlaylistChecks { get; } = [];
        public int Writes { get; private set; }
        public int Touches { get; private set; }
        public int DirectoryCreations { get; private set; }
        public int Prepares { get; private set; }

        public string AssetIdFor(string source) => AssetId;
        public void CreateDirectories(string assetId) => DirectoryCreations++;
        public bool Exists(string assetId) => Manifest is not null;
        public int CountSegments(string assetId, OutputFolder folder) => 0;

        public Task<AssetManifest?> ReadManifestAsync(string assetId, CancellationToken ct = default)
            => Task.FromResult(Manifest);

        public Task WriteManifestAsync(string assetId, AssetManifest manifest, CancellationToken ct = default)
        {
            Writes++;
            Manifest = manifest;
            return Task.CompletedTask;
        }

        public Task TouchAsync(string assetId, CancellationToken ct = default)
        {
            Touches++;
            return Task.CompletedTask;
        }

        public Task<long> TotalBytesAsync(CancellationToken ct = default) => Task.FromResult(0L);

        public Task<IReadOnlyList<string>> CollectAsync(
            long maxBytes, IReadOnlySet<string>? keep = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> ReadInternalPlaylistAsync(string assetId, OutputFolder folder, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public bool IsPlaylistComplete(string assetId, OutputFolder folder)
        {
            PlaylistChecks.Add(folder);
            return Complete.Contains(folder);
        }

        public string? FindFile(string assetId, OutputFolder folder, string fileName)
            => Files.Contains((folder, fileName)) ? fileName : null;

        public void PrepareBuild(string assetId, OutputFolder folder) => Prepares++;
    }
}
