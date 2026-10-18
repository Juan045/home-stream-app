using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Encoder;

public sealed record VideoEncoder(Func<TranscodeOptions, List<string>> BuildArgs, string? CodecString);

public static class VideoEncoders
{
    // Agregar un encoder es agregar una entrada aca; el builder y el AssetBuilder no se tocan.
    static readonly Dictionary<string, VideoEncoder> All = new()
    {
        ["h264"] = new(H264, "avc1.640029"),  // high@4.1 fijo: el string se conoce de antemano
        ["av1"] = new(Av1, null),             // sin -level fijo no se puede declarar sin mentir
    };

    public static VideoEncoder Get(string name)
        => All.TryGetValue(name, out var encoder)
            ? encoder
            : throw new ArgumentException(
                $"Codec de video desconocido: '{name}' (disponibles: {string.Join(", ", All.Keys.Order())})");

    static List<string> H264(TranscodeOptions o) =>
    [
        "-c:v", "libx264", "-preset", o.Preset, "-crf", o.Crf.ToString(),
        "-maxrate", o.VideoMaxBitrate, "-bufsize", o.VideoBufsize,
        "-pix_fmt", "yuv420p", "-profile:v", "high", "-level", "4.1",
        .. Scale(o), .. KeyFrames(o), "-sc_threshold", "0",
    ];

    // Sin -maxrate/-bufsize/-profile/-level a proposito (ver CodecString null arriba).
    static List<string> Av1(TranscodeOptions o) =>
    [
        "-c:v", "libsvtav1", "-preset", SvtAv1Preset(o.Preset), "-crf", o.Crf.ToString(),
        "-pix_fmt", "yuv420p", .. Scale(o), .. KeyFrames(o),
    ];

    static string[] Scale(TranscodeOptions o)
        => o.VideoMaxHeight is { } h ? ["-vf", $"scale=-2:'min({h},ih)'"] : [];

    // Un keyframe exacto en cada borde de segmento.
    static string[] KeyFrames(TranscodeOptions o)
        => ["-force_key_frames", $"expr:gte(t,n_forced*{o.HlsTime})"];

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
