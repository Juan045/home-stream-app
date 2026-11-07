using StreamMedia.Application.Encoder;
using StreamMedia.Domain.Entities;

public class TranscodeProfilesTests
{
    const string Live = "live";
    const string Archive = "archive";

    static TranscodeOptions Profile(string role, string codec)
        => role == Live ? TranscodeProfiles.ForLive(codec) : TranscodeProfiles.ForArchive(codec);

    [Fact]
    public void ForLive_h264_es_el_perfil_por_defecto()
    {
        Assert.Equal(new TranscodeOptions(), TranscodeProfiles.ForLive("h264"));
    }

    [Fact]
    public void ForArchive_av1_conserva_el_perfil_de_biblioteca()
    {
        var profile = TranscodeProfiles.ForArchive("av1");

        Assert.Equal("av1", profile.VideoCodec);
        Assert.Equal(24, profile.Crf);
        Assert.Equal("6", profile.Preset);
        Assert.Equal(1080, profile.VideoMaxHeight);
        Assert.True(profile.ForceTranscode);
    }

    // En vivo un H.264 SDR con remux se sigue copiando, con cualquier ajuste.
    [Fact]
    public void ForLive_av1_no_fuerza_transcode()
    {
        Assert.False(TranscodeProfiles.ForLive("av1").ForceTranscode);
    }

    [Fact]
    public void ForLive_av1_sigue_la_matriz()
    {
        var profile = TranscodeProfiles.ForLive("av1");

        Assert.Equal("av1", profile.VideoCodec);
        Assert.Equal(30, profile.Crf);
        Assert.Equal("10", profile.Preset);
        Assert.Equal(720, profile.VideoMaxHeight);
    }

    [Fact]
    public void ForArchive_h264_sigue_la_matriz()
    {
        var profile = TranscodeProfiles.ForArchive("h264");

        Assert.Equal("h264", profile.VideoCodec);
        Assert.Equal(20, profile.Crf);
        Assert.Equal("slower", profile.Preset);
        Assert.Equal(1080, profile.VideoMaxHeight);
        Assert.Equal("12000k", profile.VideoMaxBitrate);
        Assert.Equal("12000k", profile.VideoBufsize);
        Assert.True(profile.ForceTranscode);
    }

    [Theory]
    [InlineData(Live, "h264", "libx264")]
    [InlineData(Live, "av1", "libsvtav1")]
    [InlineData(Archive, "h264", "libx264")]
    [InlineData(Archive, "av1", "libsvtav1")]
    public void Cada_perfil_genera_argumentos_con_su_encoder(string role, string codec, string encoder)
    {
        var args = TranscodeCommandBuilder.Video(
            "/media/pelicula.mkv", "/output/abc123/video", StreamStrategy.Transcode, Profile(role, codec));

        Assert.Equal(encoder, args[args.IndexOf("-c:v") + 1]);
    }

    public static TheoryData<string, string> PerfilesRegistrados()
    {
        var data = new TheoryData<string, string>();
        foreach (var codec in TranscodeProfiles.LiveCodecs) data.Add(Live, codec);
        foreach (var codec in TranscodeProfiles.ArchiveCodecs) data.Add(Archive, codec);
        return data;
    }

    // Cubre tambien lo que se agregue: un ["av1"] que olvida VideoCodec codificaria H.264.
    [Theory]
    [MemberData(nameof(PerfilesRegistrados))]
    public void Cada_perfil_usa_el_codec_de_su_clave_y_esta_registrado(string role, string codec)
    {
        Assert.Equal(codec, Profile(role, codec).VideoCodec);
        Assert.Contains(codec, VideoEncoders.Names);
    }

    [Theory]
    [InlineData(Live)]
    [InlineData(Archive)]
    public void Codec_desconocido_lanza_ArgumentException(string role)
    {
        var ex = Assert.Throws<ArgumentException>(() => Profile(role, "vp9"));

        Assert.Contains("vp9", ex.Message);
    }

    [Fact]
    public void Las_listas_ofrecen_h264_y_av1_en_los_dos_roles()
    {
        Assert.Contains("h264", TranscodeProfiles.LiveCodecs);
        Assert.Contains("av1", TranscodeProfiles.LiveCodecs);
        Assert.Contains("h264", TranscodeProfiles.ArchiveCodecs);
        Assert.Contains("av1", TranscodeProfiles.ArchiveCodecs);
    }
}
