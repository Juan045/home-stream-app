using StreamMedia.Application.Encoder;
using StreamMedia.Domain.Entities;

public class TranscodeCommandBuilderTests
{
    [Fact]
    public void Video_con_opciones_AV1_genera_argumentos_correctos()
    {
        // Usa el perfil real de biblioteca: si el perfil cambia, el test sigue cubriendolo.
        var args = TranscodeCommandBuilder.Video(
            "/media/pelicula.mkv", "/output/abc123/video", StreamStrategy.Transcode, TranscodeProfiles.Archive);

        // Encoder correcto
        Assert.Contains("-c:v", args);
        Assert.Contains("libsvtav1", args);

        // Invariantes del pipeline HLS (I1/I2)
        Assert.Contains("-copyts", args);
        Assert.Contains("-force_key_frames", args);
        Assert.Contains("-an", args);

        // No debe copiar el video (ForceTranscode = true)
        Assert.DoesNotContain("-c:v copy", string.Join(" ", args));

        // Prohibidos (rompen la sincronizacion audio/video)
        Assert.DoesNotContain("-output_ts_offset", args);
        Assert.DoesNotContain("-start_at_zero", args);

        // -sc_threshold es de x264, no de SVT-AV1
        Assert.DoesNotContain("-sc_threshold", args);

        // 10 bits
        Assert.Contains("-pix_fmt", args);
        Assert.Contains("yuv420p10le", args);

        Assert.Contains("-svtav1-params", args);
        Assert.Contains("tune=0:enable-variance-boost=1", args);

        // Escala por VideoMaxHeight, con lanczos
        Assert.Contains("-vf", args);
        Assert.Contains("flags=lanczos", args[args.IndexOf("-vf") + 1]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Video_AV1_tonemap_solo_si_la_fuente_es_HDR(bool hdr)
    {
        var args = TranscodeCommandBuilder.Video(
            "/media/pelicula.mkv", "/output/abc123/video", StreamStrategy.Transcode,
            TranscodeProfiles.Archive with { TonemapHdr = hdr });

        // Un solo -vf: FFmpeg ignora todos menos el ultimo
        Assert.Single(args, a => a == "-vf");
        var vf = args[args.IndexOf("-vf") + 1];

        Assert.Equal(hdr, vf.Contains("tonemap"));
        Assert.Equal(hdr, args.Contains("-color_primaries"));
        Assert.Equal(hdr, args.Contains("-color_trc"));
        Assert.Equal(hdr, args.Contains("-colorspace"));

        if (!hdr) return;

        // Escala primero, tonemap despues
        Assert.StartsWith("scale=", vf);
        Assert.True(vf.IndexOf("scale=") < vf.IndexOf("tonemap"));

        Assert.Equal("bt709", args[args.IndexOf("-color_primaries") + 1]);
        Assert.Equal("bt709", args[args.IndexOf("-color_trc") + 1]);
        Assert.Equal("bt709", args[args.IndexOf("-colorspace") + 1]);
    }

    [Fact]
    public void Video_con_opciones_H264_genera_libx264()
    {
        var h264 = new TranscodeOptions(); // defaults: h264
        var args = TranscodeCommandBuilder.Video(
            "/media/pelicula.mkv", "/output/abc123/video", StreamStrategy.Transcode, h264);

        Assert.Contains("libx264", args);
        Assert.Contains("-sc_threshold", args); // solo para x264
    }
}
