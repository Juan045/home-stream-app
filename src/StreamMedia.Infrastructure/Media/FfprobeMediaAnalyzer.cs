using System.Globalization;
using System.Text.Json;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Domain.Entities;

namespace StreamMedia.Infrastructure.Media;

public sealed class FfprobeMediaAnalyzer(FfprobeRunner runner) : IMediaAnalyzer
{
    public async Task<SourceInfo> AnalyzeAsync(string path, CancellationToken ct = default)
    {
        using var doc = await runner.ProbeAsync(path, ct);
        return Parse(doc.RootElement);
    }

    // Separado para probarlo con un JSON de ejemplo, sin ffprobe.
    public static SourceInfo Parse(JsonElement root)
    {
        JsonElement? video = null;
        var audio = new List<AudioTrack>();
        var subs = new List<SubtitleTrack>();

        foreach (var s in root.GetProperty("streams").EnumerateArray())
        {
            switch (Str(s, "codec_type"))
            {
                case "video" when video is null:
                    video = s;
                    break;
                case "audio":
                    audio.Add(new(audio.Count, Language(s), Str(s, "codec_name") ?? "unknown"));
                    break;
                case "subtitle":
                    subs.Add(new(subs.Count, Language(s)));
                    break;
            }
        }

        if (video is not { } v)
            throw new InvalidMediaException("El archivo no tiene stream de video.");
        
        // ponytail: sin duracion cae a 0; buscar en los streams si aparece un caso real.
        var duration = double.TryParse(Str(root.GetProperty("format"), "duration"),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

        return new SourceInfo(duration, Str(v, "codec_name") ?? "unknown",
            v.GetProperty("width").GetInt32(), v.GetProperty("height").GetInt32(), audio, subs);    
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.GetString() : null;

    static string Language(JsonElement s) =>
        s.TryGetProperty("tags", out var t) ? Str(t, "language") ?? "und" : "und";
}