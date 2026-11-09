using Microsoft.Extensions.Logging.Abstractions;
using StreamMedia.Application.Encoder;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.Streaming;
using StreamMedia.Application.Streaming.Interfaces;
using StreamMedia.Domain.DTO;
using StreamMedia.Domain.Entities;

public class AssetBuilderOptionsTests
{
    const string Source = "/media/pelicula.mkv";

    // Sin pistas: el unico FFmpeg que se puede lanzar es el del video.
    static readonly SourceInfo H264Sdr = new(100, "h264", 1920, 1080, [], [], ColorTransfer: "bt709");
    static readonly SourceInfo HevcSdr = new(100, "hevc", 1920, 1080, [], [], ColorTransfer: "bt709");

    static readonly TranscodeOptions LiveH264 = TranscodeProfiles.ForLive("h264");
    static readonly TranscodeOptions LiveAv1 = TranscodeProfiles.ForLive("av1");
    static readonly TranscodeOptions ArchiveAv1 = TranscodeProfiles.ForArchive("av1");

    static AssetManifest ManifestOf(
        SourceInfo info, TranscodeOptions options, StreamStrategy strategy, bool pinned = false) => new()
    {
        AssetId = "asset",
        Source = Source,
        Info = info,
        Options = options,
        Strategy = strategy,
        Pinned = pinned,
    };

    static AssetBuilder BuilderOf(FakeStore store, FakeFfmpeg ffmpeg, SourceInfo? analyzed = null) => new(
        store, new FakeAnalyzer(analyzed ?? HevcSdr), ffmpeg, new TranscodeOptions(), 8,
        NullLogger<AssetBuilder>.Instance);

    [Fact]
    public async Task Video_pendiente_toma_el_perfil_de_quien_abre()
    {
        var store = new FakeStore { Manifest = ManifestOf(HevcSdr, LiveH264, StreamStrategy.Transcode) };
        var ffmpeg = new FakeFfmpeg();
        await using var builder = BuilderOf(store, ffmpeg);

        var asset = await builder.OpenAsync(Source, IgnoredTracks.None, StreamStrategy.Transcode, LiveAv1);

        Assert.Equal("av1", asset.Options.VideoCodec);
        Assert.Equal("av1", store.Manifest!.Options!.VideoCodec);
        Assert.Contains("libsvtav1", ffmpeg.Calls.Single());
    }

    // Trampa: un encode interrumpido no puede perder su perfil porque alguien le dio Play.
    [Fact]
    public async Task Encode_interrumpido_conserva_perfil_y_pin_con_un_Play()
    {
        var store = new FakeStore { Manifest = ManifestOf(HevcSdr, ArchiveAv1, StreamStrategy.Transcode, pinned: true) };
        var ffmpeg = new FakeFfmpeg();
        await using var builder = BuilderOf(store, ffmpeg);

        var asset = await builder.OpenAsync(Source, IgnoredTracks.None, StreamStrategy.Transcode, LiveH264);

        Assert.Equal("av1", asset.Options.VideoCodec);
        Assert.True(asset.Options.ForceTranscode);
        Assert.True(asset.Pinned);
        Assert.Contains("libsvtav1", ffmpeg.Calls.Single());
    }

    [Fact]
    public async Task Encode_interrumpido_cambia_de_perfil_con_otro_encode()
    {
        var store = new FakeStore { Manifest = ManifestOf(HevcSdr, ArchiveAv1, StreamStrategy.Transcode, pinned: true) };
        var ffmpeg = new FakeFfmpeg();
        await using var builder = BuilderOf(store, ffmpeg);

        var asset = await builder.OpenAsync(
            Source, IgnoredTracks.None, StreamStrategy.Transcode, TranscodeProfiles.ForArchive("h264"), pin: true);

        Assert.Equal("h264", asset.Options.VideoCodec);
        Assert.True(asset.Pinned);
        Assert.Contains("libx264", ffmpeg.Calls.Single());
    }

    [Fact]
    public async Task Video_despachado_conserva_el_primer_perfil_sin_otro_FFmpeg()
    {
        var store = new FakeStore();
        var ffmpeg = new FakeFfmpeg();
        await using var builder = BuilderOf(store, ffmpeg);

        await builder.OpenAsync(Source, IgnoredTracks.None, StreamStrategy.Transcode, ArchiveAv1, pin: true);
        var asset = await builder.OpenAsync(Source, IgnoredTracks.None, StreamStrategy.Transcode, LiveH264);

        Assert.Equal("av1", asset.Options.VideoCodec);
        Assert.Equal("av1", store.Manifest!.Options!.VideoCodec);
        Assert.Single(ffmpeg.Calls);
    }

    // Video terminado y fuera de memoria, como despues de un reinicio. Es el Play despues del encode (I7).
    [Fact]
    public async Task Video_listo_en_disco_conserva_perfil_y_estrategia_sin_FFmpeg()
    {
        var store = new FakeStore
        {
            Manifest = ManifestOf(H264Sdr, ArchiveAv1, StreamStrategy.Remux, pinned: true),
            VideoComplete = true,
        };
        var ffmpeg = new FakeFfmpeg();
        await using var builder = BuilderOf(store, ffmpeg);

        var asset = await builder.OpenAsync(Source, IgnoredTracks.None, StreamStrategy.Transcode, LiveH264);

        Assert.Equal("av1", asset.Options.VideoCodec);
        Assert.Equal(StreamStrategy.Remux, asset.Strategy);
        Assert.True(asset.Pinned);
        Assert.Empty(ffmpeg.Calls);
    }

    [Fact]
    public async Task Video_pendiente_toma_la_estrategia_de_quien_abre()
    {
        var store = new FakeStore { Manifest = ManifestOf(H264Sdr, LiveH264, StreamStrategy.Remux) };
        var ffmpeg = new FakeFfmpeg();
        await using var builder = BuilderOf(store, ffmpeg);

        var asset = await builder.OpenAsync(Source, IgnoredTracks.None, StreamStrategy.Transcode, LiveH264);

        Assert.Equal(StreamStrategy.Transcode, asset.Strategy);
        Assert.Equal(StreamStrategy.Transcode, store.Manifest!.Strategy);
        Assert.DoesNotContain("copy", ffmpeg.Calls.Single());
    }

    // El bug de punta a punta: H.264 SDR sin procesar, se elige "Transcodificar" y se da Play.
    [Fact]
    public async Task Transcodificar_un_H264_recodifica_aunque_el_prewarm_llegue_primero()
    {
        var store = new FakeStore();
        var ffmpeg = new FakeFfmpeg();
        await using var builder = BuilderOf(store, ffmpeg, analyzed: H264Sdr);

        builder.QueuePrewarm(Source, IgnoredTracks.None);
        await store.ManifestWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(ffmpeg.Calls); // el prewarm no toca el video

        await builder.OpenAsync(Source, IgnoredTracks.None, StreamStrategy.Transcode, LiveH264);

        Assert.DoesNotContain("copy", ffmpeg.Calls.Single());
        Assert.Contains("libx264", ffmpeg.Calls.Single());
    }

    // Con Transcode sobre un H.264 tambien detecta volver a la estrategia que sale del codec.
    [Theory]
    [InlineData(StreamStrategy.Remux)]
    [InlineData(StreamStrategy.Transcode)]
    public async Task El_prewarm_no_cambia_la_estrategia_ni_el_perfil(StreamStrategy strategy)
    {
        var store = new FakeStore { Manifest = ManifestOf(H264Sdr, LiveAv1, strategy) };
        var ffmpeg = new FakeFfmpeg();
        await using var builder = BuilderOf(store, ffmpeg);

        builder.QueuePrewarm(Source, IgnoredTracks.None);
        await store.ManifestWritten.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(strategy, store.Manifest!.Strategy);
        Assert.Equal("av1", store.Manifest.Options!.VideoCodec);
        Assert.Empty(ffmpeg.Calls);
    }

    [Fact]
    public async Task Video_despachado_conserva_la_primera_estrategia()
    {
        var store = new FakeStore();
        var ffmpeg = new FakeFfmpeg();
        await using var builder = BuilderOf(store, ffmpeg, analyzed: H264Sdr);

        await builder.OpenAsync(Source, IgnoredTracks.None, StreamStrategy.Remux, LiveH264);
        var asset = await builder.OpenAsync(Source, IgnoredTracks.None, StreamStrategy.Transcode, LiveH264);

        Assert.Equal(StreamStrategy.Remux, asset.Strategy);
        Assert.Contains("copy", ffmpeg.Calls.Single());
    }

    // D13: el tonemap lo decide la fuente, diga lo que diga el perfil que llega.
    [Theory]
    [InlineData("smpte2084", false, true)]
    [InlineData("arib-std-b67", false, true)]
    [InlineData("bt709", true, false)]
    public async Task El_tonemap_lo_decide_la_fuente(string colorTransfer, bool requested, bool expected)
    {
        var info = HevcSdr with { ColorTransfer = colorTransfer };
        var store = new FakeStore { Manifest = ManifestOf(info, LiveH264, StreamStrategy.Transcode) };
        await using var builder = BuilderOf(store, new FakeFfmpeg());

        var asset = await builder.OpenAsync(
            Source, IgnoredTracks.None, StreamStrategy.Transcode, LiveAv1 with { TonemapHdr = requested });

        Assert.Equal(expected, asset.Options.TonemapHdr);
    }

    sealed class FakeAnalyzer(SourceInfo info) : IMediaAnalyzer
    {
        public Task<SourceInfo> AnalyzeAsync(string absolutePath, CancellationToken ct = default)
            => Task.FromResult(info);
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

    sealed class FakeStore : IAssetStore
    {
        public AssetManifest? Manifest { get; set; }
        public bool VideoComplete { get; init; }
        public TaskCompletionSource ManifestWritten { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string AssetIdFor(string source) => "asset";
        public AssetPaths Paths(string assetId) => new($"/cache/{assetId}");
        public AssetPaths Prepare(string assetId) => Paths(assetId);
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

        public bool IsPlaylistComplete(string directory) => VideoComplete && directory == Paths("asset").Video;
        public bool FileExists(string path) => false;
        public void PrepareBuild(string directory) { }
    }
}
