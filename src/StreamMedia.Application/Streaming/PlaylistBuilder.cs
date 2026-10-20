using System.Globalization;
using System.Text;
using StreamMedia.Domain.DTO;

namespace StreamMedia.Application.Streaming;

/// <summary>Una pista de audio declarada como #EXT-X-MEDIA en el master.</summary>
public sealed record AudioRendition(
    string Uri,
    string Name,
    string Language,
    int Channels = 2,
    string Codec = PlaylistBuilder.AacLc,
    bool Default = false);

/// <summary>
/// Construccion de las playlists HLS que ve el cliente. Logica pura: no toca disco ni FFmpeg.
/// Las duraciones #EXTINF son las reales que reporto FFmpeg: un #EXTINF que miente desfasa
/// los subtitulos y el error se acumula.
/// </summary>
public static class PlaylistBuilder
{
    public const string AacLc = "mp4a.40.2";

    const string AudioGroup = "aud";
    const int HlsVersion = 7; // fMP4 requiere >= 6; 7 por EXT-X-INDEPENDENT-SEGMENTS
    const string ExtInfPrefix = "#EXTINF:";
    const string EndListTag = "#EXT-X-ENDLIST";

    // ffprobe devuelve el nombre del perfil; HLS quiere (profile_idc, constraint_flags) en hex.
    static readonly Dictionary<string, (int Profile, int Constraints)> AvcProfiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["constrained baseline"] = (0x42, 0xE0),
            ["baseline"] = (0x42, 0xE0),
            ["main"] = (0x4D, 0x40),
            ["extended"] = (0x58, 0x00),
            ["high"] = (0x64, 0x00),
            ["high 10"] = (0x6E, 0x00),
            ["high 4:2:2"] = (0x7A, 0x00),
            ["high 4:4:4 predictive"] = (0xF4, 0x00),
        };

    static readonly Dictionary<string, int> AacProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["main"] = 1, ["lc"] = 2, ["ssr"] = 3, ["ltp"] = 4, ["he-aac"] = 5, ["he-aacv2"] = 29,
    };

    /// <summary>
    /// Duraciones reales de los #EXTINF del internal.m3u8 de FFmpeg. Ignora lineas mal formadas:
    /// FFmpeg escribe el archivo mientras corre y puede leerse a mitad de un flush.
    /// </summary>
    public static List<double> ParseInternalPlaylist(string text)
    {
        var durations = new List<double>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith(ExtInfPrefix, StringComparison.Ordinal))
                continue;

            var value = line[ExtInfPrefix.Length..].Split(',', 2)[0].Trim();
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                durations.Add(seconds);
        }
        return durations;
    }

    /// <summary>True si FFmpeg ya cerro la playlist con #EXT-X-ENDLIST.</summary>
    public static bool IsComplete(string text)
        => text.Contains(EndListTag, StringComparison.Ordinal);

    /// <summary>Entero >= al segmento mas largo, como pide la especificacion de HLS.</summary>
    public static int TargetDuration(IReadOnlyList<double> durations)
        => durations.Count == 0 ? 1 : Math.Max(1, (int)Math.Ceiling(durations.Max()));

    /// <summary>
    /// Media playlist de una pista (video o audio). Con complete=false sale como EVENT y sin
    /// ENDLIST: el build sigue corriendo y la lista va a crecer. Terminado, sale como VOD.
    /// </summary>
    public static string BuildMediaPlaylist(IReadOnlyList<double> durations, bool complete = true)
    {
        var sb = new StringBuilder();
        sb.Append("#EXTM3U\n");
        sb.Append(CultureInfo.InvariantCulture, $"#EXT-X-VERSION:{HlsVersion}\n");
        sb.Append(CultureInfo.InvariantCulture, $"#EXT-X-TARGETDURATION:{TargetDuration(durations)}\n");
        sb.Append("#EXT-X-MEDIA-SEQUENCE:0\n");
        sb.Append($"#EXT-X-PLAYLIST-TYPE:{(complete ? "VOD" : "EVENT")}\n");
        sb.Append($"#EXT-X-MAP:URI=\"{HlsLayout.InitName}\"\n");

        for (var i = 0; i < durations.Count; i++)
            sb.Append(CultureInfo.InvariantCulture, $"#EXTINF:{durations[i]:F6},\n{HlsLayout.SegmentName(i)}\n");

        if (complete)
            sb.Append(EndListTag).Append('\n');

        return sb.ToString();
    }

    /// <summary>
    /// Master con el video y las pistas de audio como renditions. Exactamente una queda
    /// DEFAULT=YES: la marcada, o la primera si ninguna lo esta.
    /// </summary>
    public static string BuildMasterPlaylist(
        string videoUri,
        IReadOnlyList<AudioRendition> renditions,
        long bandwidth,
        string? videoCodec = null,
        (int Width, int Height)? resolution = null)
    {
        var sb = new StringBuilder();
        sb.Append("#EXTM3U\n");
        sb.Append(CultureInfo.InvariantCulture, $"#EXT-X-VERSION:{HlsVersion}\n");
        sb.Append("#EXT-X-INDEPENDENT-SEGMENTS\n");

        var defaultIndex = -1;
        for (var i = 0; i < renditions.Count && defaultIndex < 0; i++)
            if (renditions[i].Default)
                defaultIndex = i;
        if (defaultIndex < 0 && renditions.Count > 0)
            defaultIndex = 0;

        for (var i = 0; i < renditions.Count; i++)
        {
            var r = renditions[i];
            sb.Append(CultureInfo.InvariantCulture,
                $"#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"{AudioGroup}\",NAME=\"{Quote(r.Name)}\"," +
                $"LANGUAGE=\"{Quote(r.Language)}\",DEFAULT={(i == defaultIndex ? "YES" : "NO")}," +
                $"AUTOSELECT=YES,CHANNELS=\"{r.Channels}\",URI=\"{r.Uri}\"\n");
        }

        var stream = new List<string> { string.Create(CultureInfo.InvariantCulture, $"BANDWIDTH={bandwidth}") };
        if (resolution is { } res)
            stream.Add(string.Create(CultureInfo.InvariantCulture, $"RESOLUTION={res.Width}x{res.Height}"));

        // O se declaran los dos codecs o ninguno. Declarar solo el del audio anuncia un variant
        // SIN video y hls.js deja la segunda pista sin SourceBuffer. Omitir CODECS es valido:
        // el player lo deduce del init segment.
        if (!string.IsNullOrEmpty(videoCodec))
        {
            var audioCodec = defaultIndex >= 0 ? renditions[defaultIndex].Codec : null;
            var declared = audioCodec is null ? videoCodec : $"{videoCodec},{audioCodec}";
            stream.Add($"CODECS=\"{declared}\"");
        }
        if (renditions.Count > 0)
            stream.Add($"AUDIO=\"{AudioGroup}\"");

        sb.Append("#EXT-X-STREAM-INF:").Append(string.Join(',', stream)).Append('\n');
        sb.Append(videoUri).Append('\n');
        return sb.ToString();
    }

    /// <summary>
    /// "avc1.640029" a partir del perfil y nivel de ffprobe. Devuelve null si los datos no
    /// alcanzan: CODECS es opcional y es mejor omitirlo que declararlo mal.
    /// </summary>
    public static string? AvcCodecString(string? profile, int? level)
    {
        if (string.IsNullOrWhiteSpace(profile) || level is not { } value)
            return null;
        if (!AvcProfiles.TryGetValue(profile.Trim(), out var codes))
            return null;
        if (value is <= 0 or > 0xFF)
            return null;

        return $"avc1.{codes.Profile:x2}{codes.Constraints:x2}{value:x2}";
    }

    /// <summary>"mp4a.40.N" segun el perfil AAC. LC por defecto.</summary>
    public static string AacCodecString(string? profile = null)
    {
        if (string.IsNullOrWhiteSpace(profile))
            return AacLc;
        return $"mp4a.40.{(AacProfiles.TryGetValue(profile.Trim(), out var n) ? n : 2)}";
    }

    // Los titulos vienen de la metadata del archivo: dejar el texto usable dentro de comillas HLS.
    static string Quote(string value)
        => string.Concat(value.Replace('"', '\'').Where(ch => ch >= ' ' && ch != '\x7f'));
}