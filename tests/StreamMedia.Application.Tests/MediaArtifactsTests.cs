using StreamMedia.Application.Artifacts;
using StreamMedia.Application.Cache;
using StreamMedia.Application.Encoder;
using StreamMedia.Application.Streaming.Interfaces;
using StreamMedia.Domain.DTO;
using StreamMedia.Domain.Entities;

public class MediaArtifactsTests
{
    const string AssetId = "asset";
    const string VttName = "sub_0_spa.vtt";

    // Una pista de cada tipo ignorada: en ningun caso puede aparecer en la respuesta.
    static readonly SourceInfo Info = new(
        100, "hevc", 1920, 1080,
        [new AudioTrack(0, "spa", "aac"), new AudioTrack(1, "eng", "aac", Ignore: true)],
        [new SubtitleTrack(0, "spa", "subrip"), new SubtitleTrack(1, "eng", "subrip", Ignore: true)],
        ColorTransfer: "bt709");

    static readonly SourceInfo H264 = Info with { VideoCodec = "h264" };

    static readonly Media Ficha = Media.FromSource("peliculas/pelicula.mkv", Info, AssetId);

    static Asset AssetOf(SourceInfo? info = null) => new(AssetId, "/media/pelicula.mkv", info ?? Info)
    {
        Options = TranscodeProfiles.ForLive("h264"),
        Strategy = StreamStrategy.Transcode,
    };

    static AssetManifest ManifestOf(
        ArtifactState video = ArtifactState.Pending,
        string? videoError = null,
        TranscodeOptions? options = null,
        bool pinned = false,
        Dictionary<int, ArtifactState>? audio = null,
        Dictionary<int, string>? audioErrors = null,
        Dictionary<int, string>? subtitles = null,
        List<int>? subtitlesFailed = null,
        Dictionary<int, string>? subtitleErrors = null) => new()
    {
        AssetId = AssetId,
        Info = Info,
        Strategy = StreamStrategy.Transcode,
        Video = video,
        VideoError = videoError,
        Options = options,
        Pinned = pinned,
        Audio = audio ?? [],
        AudioErrors = audioErrors ?? [],
        Subtitles = subtitles ?? [],
        SubtitlesFailed = subtitlesFailed ?? [],
        SubtitleErrors = subtitleErrors ?? [],
    };

    // Lo que el caso espera y lo que recibe FromManifest salen del mismo layout: asi coinciden tambien en Windows.
    static readonly CacheLayout Layout = new("/cache");

    static MediaArtifacts Cold(AssetManifest manifest, FakeStore? store = null)
        => MediaArtifacts.FromManifest(Ficha, AssetId, manifest, store ?? new FakeStore(), Layout.Paths(AssetId));

    // --- Sin nada generado -------------------------------------------------------

    [Fact]
    public void Idle_tiene_el_video_sin_procesar_y_las_pistas_pendientes()
    {
        var result = MediaArtifacts.Idle(Ficha);

        Assert.Equal(Ficha.Id, result.IdMedia);
        Assert.Equal(AssetId, result.AssetId);
        Assert.False(result.Pinned);
        Assert.Null(result.VideoCodec);
        Assert.Equal(new ArtifactStatus(null, "idle", 0, false, null), result.Video);
        Assert.Equal(new ArtifactStatus(0, "pending", 0, false, null), Assert.Single(result.Audio));
        Assert.Equal(new ArtifactStatus(0, "pending", null, false, null), Assert.Single(result.Subtitles));
    }

    // --- En memoria ----------------------------------------------------------------

    [Theory]
    [InlineData(ArtifactState.Ready, 0, 100, 1)]
    [InlineData(ArtifactState.Building, 50, 100, 0.5)]
    [InlineData(ArtifactState.Building, 150, 100, 1)] // FFmpeg puede pasarse de la duracion que dijo ffprobe
    [InlineData(ArtifactState.Building, 50, 0, 0)]    // sin duracion conocida no se puede dividir
    [InlineData(ArtifactState.Pending, 0, 100, 0)]
    [InlineData(ArtifactState.Failed, 40, 100, 0)]
    public void Audio_en_memoria_informa_su_avance(ArtifactState state, double seconds, double duration, double expected)
    {
        var asset = AssetOf(Info with { Duration = duration });
        asset.Audio[0] = new Artifact { State = state, SecondsDone = seconds, Started = true };

        var audio = Assert.Single(MediaArtifacts.FromAsset(Ficha, asset).Audio);

        Assert.Equal(expected, audio.Progress);
    }

    [Fact]
    public void Video_en_memoria_informa_su_avance()
    {
        var asset = AssetOf();
        asset.Video.State = ArtifactState.Building;
        asset.Video.Started = true;
        asset.Video.SecondsDone = 25;

        var result = MediaArtifacts.FromAsset(Ficha, asset);

        Assert.Equal(new ArtifactStatus(null, "building", 0.25, false, null), result.Video);
    }

    [Fact]
    public void Un_artefacto_sin_slot_figura_en_cola()
    {
        var asset = AssetOf();
        asset.Audio[0] = new Artifact { State = ArtifactState.Building };

        var audio = Assert.Single(MediaArtifacts.FromAsset(Ficha, asset).Audio);

        Assert.Equal("building", audio.State);
        Assert.True(audio.Queued);
    }

    // Un subtitulo en curso no reporta avance: informar 0 seria decir que no arranco.
    [Fact]
    public void Un_subtitulo_en_curso_no_informa_avance_ni_cola()
    {
        var asset = AssetOf();
        var subtitle = new SubtitleArtifact(VttName);
        subtitle.Artifact.State = ArtifactState.Building;
        subtitle.Artifact.Started = true;
        asset.Subtitles[0] = subtitle;

        var status = Assert.Single(MediaArtifacts.FromAsset(Ficha, asset).Subtitles);

        Assert.Equal(new ArtifactStatus(0, "building", null, false, null), status);
    }

    [Fact]
    public void Un_subtitulo_listo_informa_avance_completo()
    {
        var asset = AssetOf();
        var subtitle = new SubtitleArtifact(VttName);
        subtitle.Artifact.State = ArtifactState.Ready;
        asset.Subtitles[0] = subtitle;

        Assert.Equal(1.0, Assert.Single(MediaArtifacts.FromAsset(Ficha, asset).Subtitles).Progress);
    }

    [Fact]
    public void Un_fallo_en_memoria_informa_su_motivo()
    {
        var asset = AssetOf();
        asset.Audio[0] = new Artifact { State = ArtifactState.Failed, Error = "stderr", Started = true };

        var audio = Assert.Single(MediaArtifacts.FromAsset(Ficha, asset).Audio);

        Assert.Equal("failed", audio.State);
        Assert.Equal("stderr", audio.Error);
    }

    // PATCH guardado y prewarm sin correr: la ficha ya pide la pista, el asset todavia no la tiene.
    [Fact]
    public void Una_pista_de_la_ficha_sin_artefacto_esta_pendiente()
    {
        var result = MediaArtifacts.FromAsset(Ficha, AssetOf());

        Assert.Equal(new ArtifactStatus(0, "pending", 0, false, null), Assert.Single(result.Audio));
        Assert.Equal(new ArtifactStatus(0, "pending", null, false, null), Assert.Single(result.Subtitles));
    }

    // La ficha manda: una pista ignorada no aparece aunque el asset todavia la tenga.
    [Fact]
    public void Una_pista_ignorada_no_aparece_aunque_el_asset_la_tenga()
    {
        var asset = AssetOf();
        asset.Audio[1] = new Artifact { State = ArtifactState.Ready };

        var result = MediaArtifacts.FromAsset(Ficha, asset);

        Assert.Equal(0, Assert.Single(result.Audio).Index);
    }

    [Fact]
    public void Video_pendiente_en_memoria_no_informa_codec()
    {
        Assert.Null(MediaArtifacts.FromAsset(Ficha, AssetOf()).VideoCodec);
    }

    [Theory]
    [InlineData(ArtifactState.Building)]
    [InlineData(ArtifactState.Ready)]
    [InlineData(ArtifactState.Failed)]
    public void Video_despachado_informa_el_codec_del_perfil(ArtifactState state)
    {
        var asset = AssetOf();
        asset.Options = TranscodeProfiles.ForLive("av1");
        asset.Video.State = state;

        Assert.Equal("av1", MediaArtifacts.FromAsset(Ficha, asset).VideoCodec);
    }

    // Sin esto, un remux se anunciaria con el codec del perfil, que nunca se aplico.
    [Fact]
    public void Remux_listo_informa_el_codec_del_origen()
    {
        var asset = AssetOf(H264);
        asset.Options = TranscodeProfiles.ForLive("av1");
        asset.Strategy = StreamStrategy.Remux;
        asset.Video.State = ArtifactState.Ready;

        Assert.Equal("h264", MediaArtifacts.FromAsset(Ficha, asset).VideoCodec);
    }

    [Fact]
    public void El_pin_sale_del_asset()
    {
        var asset = AssetOf();
        asset.Pinned = true;

        Assert.True(MediaArtifacts.FromAsset(Ficha, asset).Pinned);
    }

    // --- Fuera de memoria ------------------------------------------------------------

    [Fact]
    public void Video_en_building_sin_playlist_cerrada_esta_interrumpido()
    {
        var result = Cold(ManifestOf(video: ArtifactState.Building, options: TranscodeProfiles.ForLive("h264")));

        Assert.Equal(new ArtifactStatus(null, "interrupted", 0, false, null), result.Video);
        Assert.Null(result.VideoCodec); // lo interrumpido no quedo en disco
    }

    // La playlist que cuenta es la del video: si mirara otra carpeta, no llegaria a "ready".
    [Fact]
    public void Video_en_building_con_su_playlist_cerrada_esta_listo()
    {
        var store = new FakeStore();
        store.CompletePlaylists.Add(Layout.Paths(AssetId).Video);

        var result = Cold(ManifestOf(video: ArtifactState.Building, options: TranscodeProfiles.ForLive("h264")), store);

        Assert.Equal(new ArtifactStatus(null, "ready", 1, false, null), result.Video);
        Assert.Equal("h264", result.VideoCodec);
    }

    [Fact]
    public void Video_fallido_conserva_su_motivo_y_su_codec()
    {
        var result = Cold(ManifestOf(
            video: ArtifactState.Failed, videoError: "stderr", options: TranscodeProfiles.ForLive("av1")));

        Assert.Equal(new ArtifactStatus(null, "failed", 0, false, "stderr"), result.Video);
        Assert.Equal("av1", result.VideoCodec);
    }

    [Fact]
    public void Manifest_sin_opciones_no_informa_codec()
    {
        Assert.Null(Cold(ManifestOf(video: ArtifactState.Ready)).VideoCodec);
    }

    [Fact]
    public void Video_pendiente_no_informa_codec()
    {
        Assert.Null(Cold(ManifestOf(options: TranscodeProfiles.ForLive("h264"))).VideoCodec);
    }

    [Fact]
    public void Audio_fallido_conserva_su_motivo()
    {
        var result = Cold(ManifestOf(
            audio: new() { [0] = ArtifactState.Failed },
            audioErrors: new() { [0] = "stderr" }));

        Assert.Equal(new ArtifactStatus(0, "failed", 0, false, "stderr"), Assert.Single(result.Audio));
    }

    // El motivo solo acompana a un fallo: si la pista no figura fallida, ese error ya no aplica.
    [Fact]
    public void Un_error_de_una_pista_que_no_fallo_no_se_informa()
    {
        var result = Cold(ManifestOf(
            audio: new() { [0] = ArtifactState.Pending },
            audioErrors: new() { [0] = "viejo" }));

        Assert.Equal(new ArtifactStatus(0, "pending", 0, false, null), Assert.Single(result.Audio));
    }

    [Fact]
    public void Audio_que_no_figura_en_el_manifest_mira_la_playlist_de_su_pista()
    {
        var store = new FakeStore();
        store.CompletePlaylists.Add(Layout.Paths(AssetId).Audio(0));

        var result = Cold(ManifestOf(), store);

        Assert.Equal(new ArtifactStatus(0, "ready", 1, false, null), Assert.Single(result.Audio));
        Assert.Equal("pending", result.Video.State); // la playlist del audio no cuenta para el video
    }

    // Busca el .vtt que dice el manifest, en la carpeta de subtitulos del asset.
    [Fact]
    public void Subtitulo_listo_con_su_archivo_en_la_carpeta_de_subtitulos()
    {
        var store = new FakeStore();
        store.ExistingFiles.Add(Path.Combine(Layout.Paths(AssetId).Subs, VttName));

        var result = Cold(ManifestOf(subtitles: new() { [0] = VttName }), store);

        Assert.Equal(new ArtifactStatus(0, "ready", 1, false, null), Assert.Single(result.Subtitles));
    }

    [Fact]
    public void Subtitulo_cuyo_archivo_ya_no_esta_queda_pendiente_sin_avance()
    {
        var result = Cold(ManifestOf(subtitles: new() { [0] = VttName }));

        Assert.Equal(new ArtifactStatus(0, "pending", null, false, null), Assert.Single(result.Subtitles));
    }

    [Fact]
    public void Subtitulo_fallido_conserva_su_motivo()
    {
        var result = Cold(ManifestOf(subtitlesFailed: [0], subtitleErrors: new() { [0] = "bitmap" }));

        Assert.Equal(new ArtifactStatus(0, "failed", null, false, "bitmap"), Assert.Single(result.Subtitles));
    }

    [Fact]
    public void Una_pista_ignorada_no_aparece_aunque_el_manifest_la_tenga()
    {
        var result = Cold(ManifestOf(
            audio: new() { [1] = ArtifactState.Ready },
            subtitles: new() { [1] = "sub_1_eng.vtt" }));

        Assert.Equal(0, Assert.Single(result.Audio).Index);
        Assert.Equal(0, Assert.Single(result.Subtitles).Index);
    }

    [Fact]
    public void El_pin_sale_del_manifest()
    {
        Assert.True(Cold(ManifestOf(pinned: true)).Pinned);
    }

    sealed class FakeStore : IAssetStore
    {
        public HashSet<string> CompletePlaylists { get; } = [];
        public HashSet<string> ExistingFiles { get; } = [];

        public bool IsPlaylistComplete(string directory) => CompletePlaylists.Contains(directory);
        public bool FileExists(string path) => ExistingFiles.Contains(path);

        public void CreateDirectories(AssetPaths paths) => throw new InvalidOperationException("no debe preparar nada");
        public void PrepareBuild(string directory) => throw new InvalidOperationException("no debe preparar nada");
        public bool Exists(string assetId) => false;
        public int CountSegments(string directory) => 0;

        public Task<AssetManifest?> ReadManifestAsync(string assetId, CancellationToken ct = default)
            => Task.FromResult<AssetManifest?>(null);

        public Task WriteManifestAsync(string assetId, AssetManifest manifest, CancellationToken ct = default)
            => throw new InvalidOperationException("no debe escribir el manifest");

        public Task TouchAsync(string assetId, CancellationToken ct = default)
            => throw new InvalidOperationException("no debe marcarlo como usado");

        public Task<long> TotalBytesAsync(CancellationToken ct = default) => Task.FromResult(0L);

        public Task<IReadOnlyList<string>> CollectAsync(
            long maxBytes, IReadOnlySet<string>? keep = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> ReadInternalPlaylistAsync(string directory, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }
}
