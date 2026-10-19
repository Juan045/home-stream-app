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

    public static TheoryData<string, bool> EncodersPorHdr()
    {
        var data = new TheoryData<string, bool>();
        foreach (var codec in VideoEncoders.Names) { data.Add(codec, true); data.Add(codec, false); }
        return data;
    }

    // El estandar: cualquier encoder registrado, hoy o manana, tonemapea HDR a SDR.
    [Theory]
    [MemberData(nameof(EncodersPorHdr))]
    public void Video_tonemap_solo_si_la_fuente_es_HDR_con_cualquier_encoder(string codec, bool hdr)
    {
        var args = TranscodeCommandBuilder.Video(
            "/media/pelicula.mkv", "/output/abc123/video", StreamStrategy.Transcode,
            new TranscodeOptions { VideoCodec = codec, TonemapHdr = hdr });

        // Un solo -vf y un solo -pix_fmt: FFmpeg ignora todos menos el ultimo
        Assert.Single(args, a => a == "-vf");
        Assert.Single(args, a => a == "-pix_fmt");
        var vf = args[args.IndexOf("-vf") + 1];

        Assert.Equal(hdr, vf.Contains("tonemap"));
        Assert.Equal(hdr, args.Contains("-color_primaries"));
        Assert.Equal(hdr, args.Contains("-color_trc"));
        Assert.Equal(hdr, args.Contains("-colorspace"));

        if (!hdr) return;

        // Escala primero, tonemap despues
        Assert.StartsWith("scale=", vf);
        Assert.True(vf.IndexOf("scale=") < vf.IndexOf("tonemap"));

        // El tonemap termina en el mismo formato que recibe el encoder
        Assert.EndsWith("format=" + args[args.IndexOf("-pix_fmt") + 1], vf);

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

    // H.264 HDR con estrategia remux: no se copia, se recodifica con tonemap.
    [Fact]
    public void Video_remux_con_HDR_recodifica()
    {
        var args = TranscodeCommandBuilder.Video(
            "/media/pelicula.mkv", "/output/abc123/video", StreamStrategy.Remux,
            new TranscodeOptions { TonemapHdr = true });

        Assert.DoesNotContain("copy", args);
        Assert.Contains("tonemap", args[args.IndexOf("-vf") + 1]);
    }
}
