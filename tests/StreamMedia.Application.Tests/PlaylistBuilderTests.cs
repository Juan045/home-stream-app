using StreamMedia.Application.Encoder;
using StreamMedia.Application.Streaming;

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
            videoCodec: VideoEncoders.Get("av1").CodecString);

        Assert.DoesNotContain("CODECS", master);
        Assert.Contains("#EXT-X-STREAM-INF:", master);
        Assert.Contains("#EXT-X-MEDIA:TYPE=AUDIO", master);
    }
}
