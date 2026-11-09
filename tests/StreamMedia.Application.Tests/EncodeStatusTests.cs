using StreamMedia.Application.Encoder;
using StreamMedia.Domain.Entities;

public class EncodeStatusTests
{
    static readonly SourceInfo H264Sdr = new(100, "h264", 1920, 1080, [], [], ColorTransfer: "bt709");
    static readonly SourceInfo HevcSdr = new(100, "hevc", 1920, 1080, [], [], ColorTransfer: "bt709");

    // El estado sale de memoria o del manifest segun si el asset esta abierto: los dos dicen lo mismo.
    static void AssertCodec(string expected, SourceInfo info, TranscodeOptions options, StreamStrategy strategy)
    {
        var asset = new Asset("asset", "/media/pelicula.mkv", info) { Options = options, Strategy = strategy };
        var manifest = new AssetManifest { Info = info, Options = options, Strategy = strategy };

        Assert.Equal(expected, EncodeStatus.From(Guid.NewGuid(), "asset", asset, null).VideoCodec);
        Assert.Equal(expected, EncodeStatus.From(Guid.NewGuid(), "asset", null, manifest).VideoCodec);
    }

    // Sin esto, la galeria muestra "Version AV1 lista" para un H.264 que solo se reempaqueto.
    [Fact]
    public void Video_copiado_informa_el_codec_del_origen()
    {
        AssertCodec("h264", H264Sdr, TranscodeProfiles.ForLive("av1"), StreamStrategy.Remux);
    }

    [Fact]
    public void Video_recodificado_informa_el_codec_del_perfil()
    {
        AssertCodec("av1", HevcSdr, TranscodeProfiles.ForLive("av1"), StreamStrategy.Transcode);
        AssertCodec("h264", HevcSdr, TranscodeProfiles.ForLive("h264"), StreamStrategy.Transcode);
    }

    // Remux con HDR no se copia: el tonemap exige recodificar.
    [Fact]
    public void Remux_con_tonemap_informa_el_codec_del_perfil()
    {
        var options = TranscodeProfiles.ForLive("av1") with { TonemapHdr = true };

        AssertCodec("av1", H264Sdr, options, StreamStrategy.Remux);
    }

    // Biblioteca fuerza el transcode: la estrategia remux de la ficha no lo copia.
    [Fact]
    public void Remux_con_biblioteca_informa_el_codec_del_perfil()
    {
        AssertCodec("av1", H264Sdr, TranscodeProfiles.ForArchive("av1"), StreamStrategy.Remux);
    }

    [Fact]
    public void Manifest_sin_opciones_no_informa_codec()
    {
        var manifest = new AssetManifest { Info = H264Sdr, Strategy = StreamStrategy.Remux };

        Assert.Null(EncodeStatus.From(Guid.NewGuid(), "asset", null, manifest).VideoCodec);
    }

    [Fact]
    public void Sin_asset_ni_manifest_es_idle_sin_codec()
    {
        var status = EncodeStatus.From(Guid.NewGuid(), "asset", null, null);

        Assert.Equal("idle", status.State);
        Assert.Null(status.VideoCodec);
    }
}
