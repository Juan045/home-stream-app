using StreamMedia.Application.Artifacts;
using StreamMedia.Application.Encoder;
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

    // Lo que pasa fuera de memoria (manifest x disco) lo cubren ArtifactRecoveryTests y
    // AssetBuilderPeekTests: aca llega siempre un Asset, abierto o reconstruido.

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

    // --- Desde un asset -------------------------------------------------------------

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

    // Abierto por un prewarm o reconstruido del manifest: un corte se ve igual (C11). Lo interrumpido
    // no quedo en disco, asi que no hay avance ni codec que informar.
    [Fact]
    public void Un_video_interrumpido_figura_interrumpido_sin_avance_ni_codec()
    {
        var asset = AssetOf();
        asset.Options = TranscodeProfiles.ForArchive("av1");
        asset.Video.Interrupted = true;

        var result = MediaArtifacts.FromAsset(Ficha, asset);

        Assert.Equal(new ArtifactStatus(null, "interrupted", 0, false, null), result.Video);
        Assert.Null(result.VideoCodec);
    }

    [Fact]
    public void Un_audio_interrumpido_figura_interrumpido()
    {
        var asset = AssetOf();
        asset.Audio[0] = new Artifact { Interrupted = true };

        Assert.Equal(new ArtifactStatus(0, "interrupted", 0, false, null), Assert.Single(MediaArtifacts.FromAsset(Ficha, asset).Audio));
    }
}
