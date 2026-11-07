using System.Text.Json;
using StreamMedia.Domain.Entities;
using StreamMedia.WebApi.Contracts;

// El contrato que consume /settings/. Sin servidor: usa la misma politica de nombres de Program.cs,
// asi que no cubre routing, status codes ni el manejador de errores.
public class EncodingSettingsContractTests
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    static string[] Codecs(JsonElement json, string property)
        => [.. json.GetProperty(property).EnumerateArray().Select(codec => codec.GetString()!)];

    [Fact]
    public void La_respuesta_tiene_el_ajuste_las_opciones_y_los_valores_por_defecto()
    {
        var response = EncodingSettingsResponse.From(new EncodingSettings("av1", "h264"));

        var json = JsonSerializer.SerializeToElement(response, Json);

        Assert.Equal(6, json.EnumerateObject().Count());
        Assert.Equal("av1", json.GetProperty("live_codec").GetString());
        Assert.Equal("h264", json.GetProperty("archive_codec").GetString());
        Assert.Equal(new[] { "h264", "av1" }, Codecs(json, "live_codecs"));
        Assert.Equal(new[] { "h264", "av1" }, Codecs(json, "archive_codecs"));
        Assert.Equal("h264", json.GetProperty("default_live_codec").GetString());
        Assert.Equal("av1", json.GetProperty("default_archive_codec").GetString());
    }

    [Fact]
    public void El_request_lee_los_dos_campos_en_snake_case()
    {
        var request = JsonSerializer.Deserialize<UpdateEncodingSettingsRequest>(
            """{ "live_codec": "av1", "archive_codec": "h264" }""", Json);

        Assert.Equal(new UpdateEncodingSettingsRequest("av1", "h264"), request);
    }

    // Tiene que llegar null al use case, que responde 422 en el formato del proyecto.
    [Fact]
    public void Un_campo_ausente_llega_como_null()
    {
        var request = JsonSerializer.Deserialize<UpdateEncodingSettingsRequest>(
            """{ "live_codec": "av1" }""", Json);

        Assert.Equal(new UpdateEncodingSettingsRequest("av1", null), request);
    }
}
