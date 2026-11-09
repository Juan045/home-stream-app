using Microsoft.Extensions.Logging.Abstractions;
using StreamMedia.Application.Cache;
using StreamMedia.Application.Cache.Interfaces;
using StreamMedia.Application.Encoder;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Application.Streaming;
using StreamMedia.Application.Streaming.Interfaces;
using StreamMedia.Domain.DTO;
using StreamMedia.Domain.Entities;

public class AssetBuilderErrorPersistenceTests
{
    const string Source = "/media/pelicula.mkv";
    const string VideoMap = "0:v:0";
    const string FirstAudioMap = "0:a:0";
    const string SecondAudioMap = "0:a:1";
    const string SubtitleMap = "0:s:0";

    static readonly SourceInfo Info = new(
        100, "hevc", 1920, 1080,
        [new AudioTrack(0, "spa", "aac"), new AudioTrack(1, "eng", "aac")],
        [new SubtitleTrack(0, "spa", "hdmv_pgs_subtitle")],
        ColorTransfer: "bt709");

    static readonly TranscodeOptions Live = TranscodeProfiles.ForLive("h264");

    static AssetManifest ManifestOf(
        ArtifactState video = ArtifactState.Pending,
        string? videoError = null,
        Dictionary<int, ArtifactState>? audio = null,
        Dictionary<int, string>? audioErrors = null,
        List<int>? subtitlesFailed = null,
        Dictionary<int, string>? subtitleErrors = null) => new()
    {
        AssetId = "asset",
        Source = Source,
        Info = Info,
        Options = Live,
        Strategy = StreamStrategy.Transcode,
        Video = video,
        VideoError = videoError,
        Audio = audio ?? [],
        AudioErrors = audioErrors ?? [],
        SubtitlesFailed = subtitlesFailed ?? [],
        SubtitleErrors = subtitleErrors ?? [],
    };

    static readonly CacheLayout Layout = new("/cache");

    static AssetBuilder BuilderOf(FakeStore store, FakeFfmpeg ffmpeg) => new(
        store, store, store, store, Layout, new FakeAnalyzer(), ffmpeg, new TranscodeOptions(), 8, NullLogger<AssetBuilder>.Instance);

    // DisposeAsync espera todas las tareas: al volver, el manifest ya tiene el resultado de cada build.
    static async Task<Asset> OpenUntilBuildsEndAsync(FakeStore store, FakeFfmpeg ffmpeg)
    {
        var builder = BuilderOf(store, ffmpeg);
        var asset = await builder.OpenAsync(Source, IgnoredTracks.None, StreamStrategy.Transcode, Live);
        await builder.DisposeAsync();
        return asset;
    }

    [Fact]
    public async Task Falla_el_video_y_el_manifest_guarda_su_stderr()
    {
        var store = new FakeStore();

        await OpenUntilBuildsEndAsync(store, new FakeFfmpeg(VideoMap));

        Assert.Equal(FakeFfmpeg.StderrOf(VideoMap), store.Manifest!.VideoError);
        Assert.Empty(store.Manifest.AudioErrors);
        Assert.Empty(store.Manifest.SubtitleErrors);
    }

    [Fact]
    public async Task Falla_un_audio_y_solo_se_guarda_el_de_esa_pista()
    {
        var store = new FakeStore();

        await OpenUntilBuildsEndAsync(store, new FakeFfmpeg(SecondAudioMap));

        var error = Assert.Single(store.Manifest!.AudioErrors);
        Assert.Equal(1, error.Key);
        Assert.Equal(FakeFfmpeg.StderrOf(SecondAudioMap), error.Value);
        Assert.Null(store.Manifest.VideoError);
    }

    [Fact]
    public async Task Falla_un_subtitulo_y_se_guardan_su_stderr_y_su_indice()
    {
        var store = new FakeStore();

        await OpenUntilBuildsEndAsync(store, new FakeFfmpeg(SubtitleMap));

        Assert.Equal(FakeFfmpeg.StderrOf(SubtitleMap), store.Manifest!.SubtitleErrors[0]);
        Assert.Contains(0, store.Manifest.SubtitlesFailed);
    }

    // Sin rehidratar, la primera escritura despues de reabrir borraria el motivo de un PGS que no se reintenta.
    [Fact]
    public async Task Subtitulo_fallido_conserva_el_motivo_al_reabrir_sin_reintentarse()
    {
        var store = new FakeStore
        {
            Manifest = ManifestOf(subtitlesFailed: [0], subtitleErrors: new() { [0] = "bitmap" }),
        };
        var ffmpeg = new FakeFfmpeg();

        var asset = await OpenUntilBuildsEndAsync(store, ffmpeg);

        var subtitle = asset.Subtitles[0].Artifact;
        Assert.Equal(ArtifactState.Failed, subtitle.State);
        Assert.Equal("bitmap", subtitle.Error);
        Assert.Equal("bitmap", store.Manifest!.SubtitleErrors[0]);
        Assert.False(ffmpeg.Ran(SubtitleMap));
    }

    [Fact]
    public async Task Subtitulo_fallido_de_un_manifest_viejo_sigue_fallido_y_sin_motivo()
    {
        var store = new FakeStore { Manifest = ManifestOf(subtitlesFailed: [0]) };
        var ffmpeg = new FakeFfmpeg();

        var asset = await OpenUntilBuildsEndAsync(store, ffmpeg);

        Assert.Equal(ArtifactState.Failed, asset.Subtitles[0].Artifact.State);
        Assert.Null(asset.Subtitles[0].Artifact.Error);
        Assert.Empty(store.Manifest!.SubtitleErrors);
        Assert.False(ffmpeg.Ran(SubtitleMap));
    }

    [Fact]
    public async Task Audio_fallido_se_relanza_al_reabrir_y_pierde_el_error_viejo()
    {
        var store = new FakeStore
        {
            Manifest = ManifestOf(
                audio: new() { [0] = ArtifactState.Failed },
                audioErrors: new() { [0] = "viejo" }),
        };
        var ffmpeg = new FakeFfmpeg();

        var asset = await OpenUntilBuildsEndAsync(store, ffmpeg);

        Assert.True(ffmpeg.Ran(FirstAudioMap));
        Assert.Equal(ArtifactState.Ready, asset.Audio[0].State);
        Assert.Empty(store.Manifest!.AudioErrors);
    }

    // Decision de diseno: el prewarm no lanza el video, pero escribe el manifest. El video
    // deja de figurar fallido, asi que su motivo ya no aplica aunque nadie lo haya reintentado.
    [Fact]
    public async Task Video_fallido_con_un_prewarm_queda_pendiente_y_sin_motivo()
    {
        var store = new FakeStore
        {
            Manifest = ManifestOf(video: ArtifactState.Failed, videoError: "viejo"),
        };
        var ffmpeg = new FakeFfmpeg();
        var builder = BuilderOf(store, ffmpeg);

        builder.QueuePrewarm(Source, IgnoredTracks.None);
        await store.ManifestWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await builder.DisposeAsync();

        Assert.Equal(ArtifactState.Pending, store.Manifest!.Video);
        Assert.Null(store.Manifest.VideoError);
        Assert.False(ffmpeg.Ran(VideoMap));
    }

    sealed class FakeAnalyzer : IMediaAnalyzer
    {
        public Task<SourceInfo> AnalyzeAsync(string absolutePath, CancellationToken ct = default)
            => Task.FromResult(Info);
    }

    // Termina enseguida, y falla solo en las pistas indicadas por su -map.
    sealed class FakeFfmpeg(params string[] failingMaps) : IFfmpegRunner
    {
        readonly List<IReadOnlyList<string>> _calls = [];

        public static string StderrOf(string map) => $"stderr de {map}";

        public bool Ran(string map)
        {
            lock (_calls) return _calls.Any(args => args.Contains(map));
        }

        public Task RunAsync(
            IReadOnlyList<string> args, Action<double>? onProgress = null, CancellationToken ct = default)
        {
            lock (_calls) _calls.Add(args);

            var failing = failingMaps.FirstOrDefault(map => args.Contains(map));
            return failing is null
                ? Task.CompletedTask
                : Task.FromException(new FfmpegException(1, StderrOf(failing)));
        }
    }

    sealed class FakeStore : IAssetStore, ISourceIdentity, IHlsOutput, ICacheSpace
    {
        public AssetManifest? Manifest { get; set; }
        public TaskCompletionSource ManifestWritten { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string AssetIdFor(string source) => "asset";
        public void CreateDirectories(AssetPaths paths) { }
        public bool Exists(string assetId) => Manifest is not null;
        public int CountSegments(string directory) => 0;

        public Task<AssetManifest?> ReadManifestAsync(string assetId, CancellationToken ct = default)
            => Task.FromResult(Manifest);

        public Task WriteManifestAsync(string assetId, AssetManifest manifest, CancellationToken ct = default)
        {
            Manifest = manifest;
            ManifestWritten.TrySetResult();
            return Task.CompletedTask;
        }

        public Task TouchAsync(string assetId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> TotalBytesAsync(CancellationToken ct = default) => Task.FromResult(0L);

        public Task<IReadOnlyList<string>> CollectAsync(
            long maxBytes, IReadOnlySet<string>? keep = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> ReadInternalPlaylistAsync(string directory, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public bool IsPlaylistComplete(string directory) => false;
        public bool FileExists(string path) => false;
        public void PrepareBuild(string directory) { }
    }
}
