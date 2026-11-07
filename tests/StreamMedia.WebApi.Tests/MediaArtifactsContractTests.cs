using System.Text.Json;
using StreamMedia.Application.Artifacts;
using StreamMedia.WebApi.Contracts;

// El contrato que consume el dialogo "Ver estado". Sin servidor: misma politica de nombres de Program.cs.
public class MediaArtifactsContractTests
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    static readonly Guid Id = Guid.NewGuid();

    static JsonElement Serialize() => JsonSerializer.SerializeToElement(
        MediaArtifactsResponse.From(new MediaArtifacts(
            Id,
            "asset",
            Pinned: true,
            VideoCodec: "av1",
            new ArtifactStatus(null, "building", 0.5, false, null),
            [new ArtifactStatus(0, "ready", 1, false, null)],
            [new ArtifactStatus(2, "failed", null, false, "stderr")])),
        Json);

    [Fact]
    public void La_respuesta_tiene_los_campos_de_la_ficha_en_snake_case()
    {
        var json = Serialize();

        Assert.Equal(7, json.EnumerateObject().Count());
        Assert.Equal(Id, json.GetProperty("id_media").GetGuid());
        Assert.Equal("asset", json.GetProperty("asset_id").GetString());
        Assert.True(json.GetProperty("pinned").GetBoolean());
        Assert.Equal("av1", json.GetProperty("video_codec").GetString());
    }

    // Tienen que viajar como null, no faltar: el front distingue "sin avance" de un 0.
    [Fact]
    public void Cada_artefacto_informa_todos_sus_campos_aunque_sean_null()
    {
        var json = Serialize();

        var video = json.GetProperty("video");
        Assert.Equal(5, video.EnumerateObject().Count());
        Assert.Equal(JsonValueKind.Null, video.GetProperty("index").ValueKind);
        Assert.Equal("building", video.GetProperty("state").GetString());
        Assert.Equal(0.5, video.GetProperty("progress").GetDouble());
        Assert.False(video.GetProperty("queued").GetBoolean());
        Assert.Equal(JsonValueKind.Null, video.GetProperty("error").ValueKind);

        var subtitle = json.GetProperty("subtitles")[0];
        Assert.Equal(2, subtitle.GetProperty("index").GetInt32());
        Assert.Equal(JsonValueKind.Null, subtitle.GetProperty("progress").ValueKind);
        Assert.Equal("stderr", subtitle.GetProperty("error").GetString());

        Assert.Equal(0, json.GetProperty("audio")[0].GetProperty("index").GetInt32());
    }
}
