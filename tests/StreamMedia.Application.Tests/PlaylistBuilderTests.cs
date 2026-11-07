using StreamMedia.Application.Encoder;
using StreamMedia.Application.Streaming;
using StreamMedia.Domain.Entities;

public class PlaylistBuilderTests
{
    // Regla I4: o se declaran video y audio en CODECS, o se omite el atributo entero. AV1 no
    // declara nivel, asi que su CodecString es null; el master no debe anunciar solo el audio
    // (hls.js dejaria la segunda pista sin SourceBuffer).
    [Fact]
    public void Master_con_AV1_omite_CODECS_completo()
    {
        var master = PlaylistBuilder.BuildMasterPlaylist(
            videoUri: "video/playlist.m3u8",
            renditions: [new AudioRendition("audio/0/playlist.m3u8", "Español", "spa", Default: true)],
            bandwidth: 4_000_000,
            videoCodec: VideoEncoders.Get("av1").CodecString(new TranscodeOptions { VideoCodec = "av1" }));

        Assert.DoesNotContain("CODECS", master);
        Assert.Contains("#EXT-X-STREAM-INF:", master);
        Assert.Contains("#EXT-X-MEDIA:TYPE=AUDIO", master);
    }

    [Fact]
    public void Master_con_H264_en_vivo_declara_video_y_audio()
    {
        var variant = VariantLine(new TranscodeOptions());

        Assert.Contains("avc1.64001f", variant);
        Assert.Contains(PlaylistBuilder.AacLc, variant);
    }

    [Fact]
    public void Master_con_H264_de_biblioteca_omite_CODECS_completo()
    {
        var variant = VariantLine(new TranscodeOptions { VideoMaxHeight = 1080, ForceTranscode = true });

        Assert.DoesNotContain("CODECS", variant);
    }

    static string VariantLine(TranscodeOptions options)
    {
        var master = PlaylistBuilder.BuildMasterPlaylist(
            videoUri: "video/playlist.m3u8",
            renditions: [new AudioRendition("audio/0/playlist.m3u8", "Español", "spa", Default: true)],
            bandwidth: 4_000_000,
            videoCodec: VideoEncoders.Get(options.VideoCodec).CodecString(options));

        return master.Split('\n').Single(line => line.StartsWith("#EXT-X-STREAM-INF:"));
    }
}
