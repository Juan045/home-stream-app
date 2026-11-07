using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Encoder;

// Lo comun a cualquier encoder lo arma BuildArgs: formato, tonemap HDR->SDR, keyframes y
// tags de color. CodecArgs y Scale (el filtro de escala para un alto maximo) son lo propio
// del codec. Asi un encoder nuevo entrega SDR sin escribir nada de color.
public sealed record VideoEncoder(
    Func<TranscodeOptions, List<string>> CodecArgs, Func<int, string> Scale,
    Func<TranscodeOptions, string?> CodecString, string PixFmt)
{
    public List<string> BuildArgs(TranscodeOptions o) =>
    [
        .. CodecArgs(o), "-pix_fmt", PixFmt,
        .. Filters(o), .. KeyFrames(o),
    ];

    // La salida siempre es SDR BT.709 (el HDR pasa por el tonemap). Sin los tags el
    // reproductor no sabe que ya es SDR y puede tomarlo como HDR. Van en el filtro y no
    // como -color_primaries & co.: FFmpeg 9 toma el color de los frames e ignora esas opciones.
    const string Bt709Tags = "setparams=color_primaries=bt709:color_trc=bt709:colorspace=bt709:range=tv";

    // Un solo -vf (FFmpeg se queda con el ultimo): primero escala, despues tonemap, que asi
    // trabaja sobre menos pixeles. El tonemap termina en el formato del encoder.
    string[] Filters(TranscodeOptions o)
    {
        List<string> filters = [];
        if (o.VideoMaxHeight is { } h) filters.Add(Scale(h));
        if (o.TonemapHdr)
            filters.Add("zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709," +
                        $"tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv,format={PixFmt}");
        filters.Add(Bt709Tags);
        return ["-vf", string.Join(',', filters)];
    }

    // Un keyframe exacto en cada borde de segmento.
    static string[] KeyFrames(TranscodeOptions o)
        => ["-force_key_frames", $"expr:gte(t,n_forced*{o.HlsTime})"];
}

public static class VideoEncoders
{
    // Agregar un encoder es agregar una entrada aca: hereda keyframes y el tonemap
    // HDR->SDR de VideoEncoder.BuildArgs. El builder y el AssetBuilder no se tocan.
    static readonly Dictionary<string, VideoEncoder> All = new()
    {
        ["h264"] = new(H264, H264Scale, H264CodecString, "yuv420p"),
        ["av1"] = new(Av1, h => $"scale=-2:'min({h},ih)':flags=lanczos", _ => null, "yuv420p10le"),
    };

    // FFmpeg: min() acepta solo dos argumentos.
    static string H264Scale(int h) =>
        $"scale='trunc(min({h * 16 / 9},min({h}*dar,iw*sar))/2)*2':'trunc(ow/dar/2)*2':flags=lanczos,setsar=1";

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
        "-profile:v", "high", "-level:v", H264Level(o), "-sc_threshold", "0",
        "-x264-params", "aq-mode=3:aq-strength=0.9:psy-rd=1.0,0.15:deblock=-1,-1:ref=4:bframes=5:rc-lookahead=60",
    ];

    // High@3.1 alcanza para 720p; la copia de biblioteca es de 1080p y pide 4.1.
    // ponytail: no mira los fps. 60 fps exceden el nivel, como ya pasa hoy con 720p60.
    // Upgrade: calcular el nivel con los fps de la fuente.
    static string H264Level(TranscodeOptions o) => o.VideoMaxHeight is > 720 ? "4.1" : "3.1";

    // Este string anuncia nivel 3.1: por encima de 720p no coincidiria con H264Level (I4).
    // Con ForceTranscode (biblioteca) tambien se omite: el prewarm pudo copiar un audio cuyo
    // perfil no coincide con el mp4a.40.2 que declararia el master.
    static string? H264CodecString(TranscodeOptions o)
        => o.ForceTranscode || o.VideoMaxHeight is > 720 ? null : "avc1.64001f";

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
