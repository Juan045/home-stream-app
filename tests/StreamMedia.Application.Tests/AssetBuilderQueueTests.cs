using Microsoft.Extensions.Logging.Abstractions;
using StreamMedia.Application.Encoder;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.Streaming;
using StreamMedia.Application.Streaming.Interfaces;
using StreamMedia.Domain.DTO;
using StreamMedia.Domain.Entities;

public class AssetBuilderQueueTests
{
    const string Source = "/media/pelicula.mkv";
    const string VideoMap = "0:v:0";
    const string AudioMap = "0:a:0";
    const string SubtitleMap = "0:s:0";

    static readonly TranscodeOptions Live = TranscodeProfiles.ForLive("h264");

    static SourceInfo InfoWith(bool subtitle = false) => new(
        100, "hevc", 1920, 1080,
        [new AudioTrack(0, "spa", "aac")],
        subtitle ? [new SubtitleTrack(0, "spa", "subrip")] : [],
        ColorTransfer: "bt709");

    // Un solo slot: mientras corre un build, los demas tienen que esperar.
    static AssetBuilder BuilderOf(SourceInfo info, FakeFfmpeg ffmpeg) => new(
        new FakeStore(), new FakeAnalyzer(info), ffmpeg, new TranscodeOptions(), 1,
        NullLogger<AssetBuilder>.Instance);

    [Fact]
    public async Task Con_un_solo_slot_el_video_corre_y_el_audio_queda_en_cola()
    {
        var ffmpeg = new FakeFfmpeg();
        await using var builder = BuilderOf(InfoWith(), ffmpeg);

        var asset = await builder.OpenAsync(Source, IgnoredTracks.None, StreamStrategy.Transcode, Live);
        await ffmpeg.WaitUntilRunningAsync(VideoMap);

        Assert.Equal(ArtifactState.Building, asset.Video.State);
        Assert.False(asset.Video.IsQueued);
        Assert.Equal(ArtifactState.Building, asset.Audio[0].State);
        Assert.True(asset.Audio[0].IsQueued);
    }

    [Fact]
    public async Task Al_terminar_el_video_el_audio_deja_la_cola()
    {
        var ffmpeg = new FakeFfmpeg();
        await using var builder = BuilderOf(InfoWith(), ffmpeg);

        var asset = await builder.OpenAsync(Source, IgnoredTracks.None, StreamStrategy.Transcode, Live);
        await ffmpeg.WaitUntilRunningAsync(VideoMap);

        ffmpeg.Finish(VideoMap);
        await ffmpeg.WaitUntilRunningAsync(AudioMap);

        Assert.Equal(ArtifactState.Building, asset.Audio[0].State);
        Assert.False(asset.Audio[0].IsQueued);
    }

    // El caso que motiva el flag: un subtitulo no reporta progreso, asi que con "building y
    // progreso 0" se veria en cola mientras corre.
    [Fact]
    public async Task Un_subtitulo_en_curso_sin_progreso_no_figura_en_cola()
    {
        var ffmpeg = new FakeFfmpeg();
        await using var builder = BuilderOf(InfoWith(subtitle: true), ffmpeg);

        var asset = await builder.OpenAsync(Source, IgnoredTracks.None, StreamStrategy.Transcode, Live);
        await ffmpeg.WaitUntilRunningAsync(VideoMap);
        ffmpeg.Finish(VideoMap);
        await ffmpeg.WaitUntilRunningAsync(AudioMap);
        ffmpeg.Finish(AudioMap);
        await ffmpeg.WaitUntilRunningAsync(SubtitleMap);

        var subtitle = asset.Subtitles[0].Artifact;
        Assert.Equal(ArtifactState.Building, subtitle.State);
        Assert.Equal(0.0, subtitle.SecondsDone);
        Assert.False(subtitle.IsQueued);
    }

    // Un artefacto solo esta en cola si esta en Building, aunque nunca haya conseguido slot.
    [Theory]
    [InlineData(ArtifactState.Pending)]
    [InlineData(ArtifactState.Ready)]
    [InlineData(ArtifactState.Failed)]
    public void Fuera_de_Building_no_esta_en_cola(ArtifactState state)
    {
        Assert.False(new Artifact { State = state }.IsQueued);
    }

    [Fact]
    public void En_Building_sin_slot_esta_en_cola_y_con_slot_no()
    {
        Assert.True(new Artifact { State = ArtifactState.Building }.IsQueued);
        Assert.False(new Artifact { State = ArtifactState.Building, Started = true }.IsQueued);
    }

    sealed class FakeAnalyzer(SourceInfo info) : IMediaAnalyzer
    {
        public Task<SourceInfo> AnalyzeAsync(string absolutePath, CancellationToken ct = default)
            => Task.FromResult(info);
    }

    // Cada build corre hasta que el test lo termina, o hasta que el builder se apaga.
    sealed class FakeFfmpeg : IFfmpegRunner
    {
        readonly Lock _gate = new();
        readonly Dictionary<string, TaskCompletionSource> _running = [];
        readonly Dictionary<string, TaskCompletionSource> _finish = [];

        public async Task RunAsync(
            IReadOnlyList<string> args, Action<double>? onProgress = null, CancellationToken ct = default)
        {
            var map = args[args.ToList().IndexOf("-map") + 1];
            Task finish;
            lock (_gate)
            {
                finish = Signal(_finish, map).Task;
                Signal(_running, map).TrySetResult();
            }
            await finish.WaitAsync(ct);
        }

        public Task WaitUntilRunningAsync(string map)
        {
            lock (_gate) return Signal(_running, map).Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public void Finish(string map)
        {
            lock (_gate) Signal(_finish, map).TrySetResult();
        }

        // RunContinuationsAsynchronously: el que completa no ejecuta la continuacion dentro del lock.
        static TaskCompletionSource Signal(Dictionary<string, TaskCompletionSource> signals, string map)
        {
            if (!signals.TryGetValue(map, out var signal))
                signals[map] = signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return signal;
        }
    }

    sealed class FakeStore : IAssetStore
    {
        public string AssetIdFor(string source) => "asset";
        public AssetPaths Paths(string assetId) => new($"/cache/{assetId}");
        public AssetPaths Prepare(string assetId) => Paths(assetId);
        public string PrepareAudio(string assetId, int track) => Paths(assetId).Audio(track);
        public bool Exists(string assetId) => false;
        public int CountSegments(string directory) => 0;

        public Task<AssetManifest?> ReadManifestAsync(string assetId, CancellationToken ct = default)
            => Task.FromResult<AssetManifest?>(null);

        public Task WriteManifestAsync(string assetId, AssetManifest manifest, CancellationToken ct = default)
            => Task.CompletedTask;

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
