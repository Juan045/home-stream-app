using Microsoft.Extensions.Logging.Abstractions;
using StreamMedia.Application.Artifacts;
using StreamMedia.Application.Cache;
using StreamMedia.Application.Cache.Interfaces;
using StreamMedia.Application.Encoder;
using StreamMedia.Application.Media;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Application.Streaming;
using StreamMedia.Application.Streaming.Interfaces;
using StreamMedia.Domain.DTO;
using StreamMedia.Domain.Entities;

public class GetMediaArtifactsUseCaseTests
{
    const string AssetId = "asset";
    const string Source = "/media/pelicula.mkv";

    static readonly SourceInfo Info = new(
        100, "hevc", 1920, 1080, [new AudioTrack(0, "spa", "aac")], [], ColorTransfer: "bt709");

    static readonly TranscodeOptions Live = TranscodeProfiles.ForLive("h264");

    static Media FichaOf(string? assetId = AssetId)
        => Media.FromSource("peliculas/pelicula.mkv", Info, assetId);

    static readonly CacheLayout Layout = new("/cache");

    static AssetBuilder BuilderOf(FakeStore store, FakeFfmpeg ffmpeg) => new(
        store, store, store, Layout, new FakeAnalyzer(), ffmpeg, new TranscodeOptions(), 8, NullLogger<AssetBuilder>.Instance);

    static GetMediaArtifactsUseCase UseCaseOf(Media? ficha, FakeStore store, AssetBuilder builder)
        => new(new FakeRepository(ficha), builder, store, store, Layout);

    [Fact]
    public async Task Una_ficha_inexistente_lanza_media_not_found()
    {
        var store = new FakeStore();
        await using var builder = BuilderOf(store, new FakeFfmpeg());

        var ex = await Assert.ThrowsAsync<MediaPathException>(
            () => UseCaseOf(null, store, builder).ExecuteAsync(Guid.NewGuid()));

        Assert.Equal("media_not_found", ex.Code);
    }

    [Fact]
    public async Task Una_ficha_sin_asset_es_idle()
    {
        var store = new FakeStore();
        await using var builder = BuilderOf(store, new FakeFfmpeg());
        var ficha = FichaOf(assetId: null);

        var result = await UseCaseOf(ficha, store, builder).ExecuteAsync(ficha.Id);

        Assert.Equal("idle", result.Video.State);
    }

    // El GC pudo haberlo borrado: la ficha conserva el id, pero ya no hay nada generado.
    [Fact]
    public async Task Un_asset_sin_manifest_es_idle()
    {
        var store = new FakeStore();
        await using var builder = BuilderOf(store, new FakeFfmpeg());
        var ficha = FichaOf();

        var result = await UseCaseOf(ficha, store, builder).ExecuteAsync(ficha.Id);

        Assert.Equal("idle", result.Video.State);
        Assert.Equal(AssetId, result.AssetId);
    }

    // La regla del panel: mirar un titulo no lanza FFmpeg, no altera el LRU ni reescribe el manifest.
    [Fact]
    public async Task Fuera_de_memoria_solo_lee()
    {
        var store = new FakeStore
        {
            Manifest = new AssetManifest { AssetId = AssetId, Info = Info, Video = ArtifactState.Ready },
        };
        var ffmpeg = new FakeFfmpeg();
        await using var builder = BuilderOf(store, ffmpeg);
        var ficha = FichaOf();

        var result = await UseCaseOf(ficha, store, builder).ExecuteAsync(ficha.Id);

        Assert.Equal("ready", result.Video.State);
        Assert.Equal(0, store.Touches);
        Assert.Equal(0, store.DirectoryCreations);
        Assert.Equal(0, store.Writes);
        Assert.Equal(0, store.AssetIdLookups); // sin stat del origen (TODO #12)
        Assert.Empty(ffmpeg.Calls);
        Assert.Null(builder.Get(AssetId));      // no lo dejo abierto
    }

    // Abrir escribe el manifest antes de lanzar los builds: ahi el video figura pending, en memoria building.
    [Fact]
    public async Task En_memoria_manda_la_memoria_y_no_toca_nada()
    {
        var store = new FakeStore();
        var ffmpeg = new FakeFfmpeg();
        await using var builder = BuilderOf(store, ffmpeg);
        await builder.OpenAsync(Source, IgnoredTracks.None, StreamStrategy.Transcode, Live);
        var ficha = FichaOf();
        var (touches, writes, calls) = (store.Touches, store.Writes, ffmpeg.Calls.Count);

        var result = await UseCaseOf(ficha, store, builder).ExecuteAsync(ficha.Id);

        Assert.Equal("building", result.Video.State);
        Assert.Equal(touches, store.Touches);
        Assert.Equal(writes, store.Writes);
        Assert.Equal(calls, ffmpeg.Calls.Count);
    }

    sealed class FakeRepository(Media? ficha) : IMediaRepository
    {
        public Task<Media?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(ficha);
        public Task<Media?> FindByPathAsync(string filePath, CancellationToken ct = default) => Task.FromResult(ficha);
        public Task AddAsync(Media media, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Media media, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Media>> ListAsync(MediaListQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Media>>([]);

        public Task<int> CountAsync(MediaListQuery query, CancellationToken ct = default) => Task.FromResult(0);
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

    sealed class FakeStore : IAssetStore, ISourceIdentity, IHlsOutput
    {
        public AssetManifest? Manifest { get; set; }
        public int Touches { get; private set; }
        public int DirectoryCreations { get; private set; }
        public int Writes { get; private set; }
        public int AssetIdLookups { get; private set; }

        public string AssetIdFor(string source)
        {
            AssetIdLookups++;
            return AssetId;
        }

        public void CreateDirectories(AssetPaths paths) => DirectoryCreations++;

        public bool Exists(string assetId) => Manifest is not null;
        public int CountSegments(string directory) => 0;

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

        public Task<string?> ReadInternalPlaylistAsync(string directory, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public bool IsPlaylistComplete(string directory) => false;
        public bool FileExists(string path) => false;
        public void PrepareBuild(string directory) { }
    }
}
