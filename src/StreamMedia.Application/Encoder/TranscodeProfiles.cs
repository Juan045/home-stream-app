using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Encoder;

// Un perfil completo por rol (en vivo o biblioteca) y codec. Cambiar solo el nombre del codec no
// alcanza: "-preset 6" no existe en libx264 y "slower" con CRF 21 haria inviable AV1 en vivo.
// Lo unico que depende de la fuente (el tonemap) lo ajusta AssetBuilder al abrir.
// Agregar un codec es agregar su entrada en VideoEncoders y en estas tablas.
public static class TranscodeProfiles
{
    static readonly Dictionary<string, TranscodeOptions> Live = new()
    {
        ["h264"] = new(),
        ["av1"] = new() { VideoCodec = "av1", Crf = 30, Preset = "10" }, // rapido: tiene que llegar a tiempo real
    };

    // ForceTranscode: sin el, un origen H.264 se copiaria con -c:v copy y el perfil quedaria
    // a merced del codec del origen en vez de producir el codec elegido.
    // Tope de 1080: la copia que se guarda para siempre no arrastra un 4K.
    static readonly Dictionary<string, TranscodeOptions> Archive = new()
    {
        ["h264"] = new()
        {
            Crf = 20, VideoMaxHeight = 1080, VideoMaxBitrate = "12000k", VideoBufsize = "12000k",
            ForceTranscode = true,
        },
        ["av1"] = new() { VideoCodec = "av1", Crf = 24, Preset = "6", VideoMaxHeight = 1080, ForceTranscode = true },
    };

    public static IEnumerable<string> LiveCodecs => Live.Keys;
    public static IEnumerable<string> ArchiveCodecs => Archive.Keys;

    public static TranscodeOptions ForLive(string codec) => Find(Live, codec);
    public static TranscodeOptions ForArchive(string codec) => Find(Archive, codec);

    static TranscodeOptions Find(Dictionary<string, TranscodeOptions> profiles, string codec)
        => profiles.TryGetValue(codec, out var profile)
            ? profile
            : throw new ArgumentException(
                $"Codec de video desconocido: '{codec}' (disponibles: {string.Join(", ", profiles.Keys.Order())})");
}
