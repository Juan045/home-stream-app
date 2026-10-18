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

        // 8 bits
        Assert.Contains("-pix_fmt", args);
        Assert.Contains("yuv420p", args);

        // Escala por VideoMaxHeight
        Assert.Contains("-vf", args);
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
