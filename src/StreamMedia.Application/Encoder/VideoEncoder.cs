using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Encoder;

// Lo comun a cualquier encoder lo arma BuildArgs: formato, escala, tonemap HDR->SDR,
// keyframes y tags de color. CodecArgs es solo lo propio del codec. Asi un encoder
// nuevo entrega SDR sin escribir nada de color.
public sealed record VideoEncoder(
    Func<TranscodeOptions, List<string>> CodecArgs, string? CodecString, string PixFmt)
{
    public List<string> BuildArgs(TranscodeOptions o) =>
    [
        .. CodecArgs(o), "-pix_fmt", PixFmt,
        .. Filters(o, PixFmt), .. KeyFrames(o),
        .. (o.TonemapHdr ? Bt709Tags : []),
    ];

    // Sin los tags el reproductor no sabe que ya es SDR y puede tomarlo como HDR.
    static readonly string[] Bt709Tags =
        ["-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709"];

    // Un solo -vf (FFmpeg se queda con el ultimo): primero escala, despues tonemap, que asi
    // trabaja sobre menos pixeles. El tonemap termina en el formato del encoder.
    static string[] Filters(TranscodeOptions o, string pixFmt)
    {
        List<string> filters = [];
        if (o.VideoMaxHeight is { } h) filters.Add($"scale=-2:'min({h},ih)':flags=lanczos");
        if (o.TonemapHdr)
            filters.Add("zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709," +
                        $"tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv,format={pixFmt}");
        return filters.Count > 0 ? ["-vf", string.Join(',', filters)] : [];
    }

    // Un keyframe exacto en cada borde de segmento.
    static string[] KeyFrames(TranscodeOptions o)
        => ["-force_key_frames", $"expr:gte(t,n_forced*{o.HlsTime})"];
}

public static class VideoEncoders
{
    // Agregar un encoder es agregar una entrada aca: hereda escala, keyframes y el tonemap
    // HDR->SDR de VideoEncoder.BuildArgs. El builder y el AssetBuilder no se tocan.
    static readonly Dictionary<string, VideoEncoder> All = new()
    {
        ["h264"] = new(H264, "avc1.640029", "yuv420p"),  // high@4.1 fijo: el string se conoce de antemano
        // Sin -level fijo no se puede declarar sin mentir.
        // 10 bits aunque la fuente sea SDR de 8: AV1 comprime mejor y evita banding.
        ["av1"] = new(Av1, null, "yuv420p10le"),
    };

    public static IEnumerable<string> Names => All.Keys;

    public static VideoEncoder Get(string name)
        => All.TryGetValue(name, out var encoder)
            ? encoder
            : throw new ArgumentException(
                $"Codec de video desconocido: '{name}' (disponibles: {string.Join(", ", Names.Order())})");

    static List<string> H264(TranscodeOptions o) =>
    [
        "-c:v", "libx264", "-preset", o.Preset, "-crf", o.Crf.ToString(),
        "-maxrate", o.VideoMaxBitrate, "-bufsize", o.VideoBufsize,
        "-profile:v", "high", "-level", "4.1", "-sc_threshold", "0",
    ];

    // Sin -maxrate/-bufsize/-profile/-level a proposito (ver CodecString null arriba).
    static List<string> Av1(TranscodeOptions o) =>
    [
        "-c:v", "libsvtav1", "-preset", SvtAv1Preset(o.Preset), "-crf", o.Crf.ToString(),
        "-svtav1-params", "tune=0:enable-variance-boost=1",
    ];

    // El preset de SVT-AV1 es un numero (0 = lento y mejor, 13 = rapido). Se traduce el nombre de x264
    // para que "Preset" signifique lo mismo con los dos encoders; un numero pasa tal cual.
    static readonly Dictionary<string, int> SvtPresets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ultrafast"] = 12, ["superfast"] = 11, ["veryfast"] = 10, ["faster"] = 9, ["fast"] = 8,
        ["medium"] = 7, ["slow"] = 5, ["slower"] = 3, ["veryslow"] = 1,
    };

    static string SvtAv1Preset(string preset)
    {
        var value = preset.Trim();
        if (value.Length > 0 && value.All(char.IsAsciiDigit)) return value;
        return (SvtPresets.TryGetValue(value, out var n) ? n : 8).ToString(); // desconocido: 8, como Python
    }
}
