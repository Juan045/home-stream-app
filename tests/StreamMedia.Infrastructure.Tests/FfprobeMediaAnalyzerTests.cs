using System.Text.Json;
using StreamMedia.Infrastructure.Media;

public class FfprobeMediaAnalyzerTests
{
    // JSON minimo de ffprobe: un stream de video y el format.
    static JsonDocument Probe(string colorTransfer) => JsonDocument.Parse($$"""
        {
          "streams": [ { "codec_type": "video", "codec_name": "hevc", "width": 3840, "height": 2160 {{colorTransfer}} } ],
          "format": { "duration": "10.0" }
        }
        """);

    [Fact]
    public void Parse_con_color_transfer_PQ_es_HDR()
    {
        using var doc = Probe(""", "color_transfer": "smpte2084" """);

        var info = FfprobeMediaAnalyzer.Parse(doc.RootElement);

        Assert.Equal("smpte2084", info.ColorTransfer);
        Assert.True(info.IsHdr());
    }

    // Sin el dato queda "unknown", no null: null es solo para fichas viejas.
    [Fact]
    public void Parse_sin_color_transfer_queda_unknown_y_no_es_HDR()
    {
        using var doc = Probe("");

        var info = FfprobeMediaAnalyzer.Parse(doc.RootElement);

        Assert.Equal("unknown", info.ColorTransfer);
        Assert.False(info.IsHdr());
    }
}
