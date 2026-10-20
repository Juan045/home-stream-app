using StreamMedia.Domain.Entities;
using StreamMedia.Domain.DTO;

namespace StreamMedia.Application.Encoder;

// Argumentos de FFmpeg. Logica pura: no ejecuta nada ni toca disco.
// Cada elemento de la lista es UN argumento (ProcessStartInfo.ArgumentList): no hay shell.
//
// Invariantes (CLAUDE.md, I1 e I2):
//  - Video, audio y subtitulos salen de corridas separadas: -copyts (antes de -i)
//    y MuxArgs son lo unico que les da el mismo origen de tiempo.
//  - NUNCA -output_ts_offset ni -start_at_zero (contradicen -copyts).
//  - NUNCA -hls_playlist_type vod (FFmpeg escribiria la playlist recien al cerrar).
public static class TranscodeCommandBuilder
{
    // Nombres de lo que produce FFmpeg. Los van a necesitar Storage y las playlists.
    /*
        const string InitName = "init.mp4";
        const string SegmentTemplate = "seg-%05d.m4s";
        const string InternalPlaylist = "internal.m3u8";
    */
    
    static readonly string[] BaseArgs =
        ["-nostdin", "-hide_banner", "-nostats", "-loglevel", "error", "-progress", "pipe:1"];
    static readonly string[] MuxArgs =
        ["-avoid_negative_ts", "disabled", "-muxdelay", "0", "-muxpreload", "0"];
    static readonly string[] DiscardExtras = ["-sn", "-dn", "-map_chapters", "-1"];

    // Video sin audio. Una corrida completa y sin -ss: el seek es del cliente.
    public static List<string> Video(string source, string outputDir, StreamStrategy strategy, TranscodeOptions options)
    {
        List<string> args = [.. BaseArgs, "-copyts", "-i", source, "-map", "0:v:0", "-an", .. DiscardExtras];

        if (options.CanCopyVideo(strategy))
            args.AddRange(["-c:v", "copy"]);
        else
            args.AddRange(VideoEncoders.Get(options.VideoCodec).BuildArgs(options));

        args.AddRange(MuxArgs);
        args.AddRange(Fmp4Output(outputDir, options.HlsTime));
        return args;
    }

    // Una pista de audio sin video.
    public static List<string> Audio(string source, string outputDir, SourceInfo info, TranscodeOptions options, int audioTrack)
    {
        if (audioTrack < 0 || audioTrack >= info.AudioTracks.Count)
            throw new ArgumentOutOfRangeException(nameof(audioTrack),
                $"Pista de audio {audioTrack} inexistente (el archivo tiene {info.AudioTracks.Count})");

        List<string> args = [.. BaseArgs, "-copyts", "-i", source, "-map", $"0:a:{audioTrack}", "-vn", .. DiscardExtras];

        if (options.CanCopyAudio(info.AudioTracks[audioTrack]))
            args.AddRange(["-c:a", "copy"]);
        else
            args.AddRange(["-c:a", "aac", "-b:a", options.AudioBitrate, "-ac", options.AudioChannels.ToString()]);

        args.AddRange(MuxArgs);
        args.AddRange(Fmp4Output(outputDir, options.HlsTime));
        return args;
    }

    // Una pista de subtitulos a WebVTT.
    public static List<string> Subtitle(string source, string outputPath, int subtitleTrack) =>
    [
        "-hide_banner", "-nostats", "-loglevel", "error",
        "-i", source, "-map", $"0:s:{subtitleTrack}", "-c:s", "webvtt", "-y", outputPath,
    ];


    // El internal.m3u8 no se sirve nunca: solo se parsea para conocer las duraciones reales.
    static List<string> Fmp4Output(string dir, int hlsTime) =>
    [
        "-f", "hls", "-hls_time", hlsTime.ToString(), "-hls_list_size", "0",
        "-hls_segment_type", "fmp4", "-hls_fmp4_init_filename", HlsLayout.InitName,
        "-hls_flags", "independent_segments",
        "-hls_segment_filename", Path.Combine(dir, HlsLayout.SegmentTemplate),
        "-y", Path.Combine(dir, HlsLayout.InternalPlaylist),
    ];
}
