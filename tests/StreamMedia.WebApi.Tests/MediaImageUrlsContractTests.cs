using System.Text.Json;
using StreamMedia.Application.Images;
using StreamMedia.Domain.Entities;
using StreamMedia.WebApi.Contracts;

// poster_url y backdrop_url en las fichas. Sin servidor: misma politica de nombres de Program.cs.
public class MediaImageUrlsContractTests
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    static readonly Guid Id = Guid.NewGuid();
    static readonly DateTimeOffset T1 = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset T2 = new(2026, 11, 8, 18, 30, 0, TimeSpan.FromHours(-3));
    static readonly SourceInfo Info = new(100, "h264", 1920, 1080, [], []);

    static Media FichaWith(DateTimeOffset? poster, DateTimeOffset? backdrop) => Media.Rehydrate(
        Id, "a.mkv", "A", null, null, Info, "film", false, StreamStrategy.Remux, null, null, null,
        posterUpdatedAt: poster, backdropUpdatedAt: backdrop);

    // Las dos respuestas tienen que dar lo mismo: una tabla por respuesta, mismos casos.
    public static TheoryData<string> Respuestas => new() { "ficha", "listado" };

    static JsonElement Serialize(string respuesta, Media ficha) => respuesta == "ficha"
        ? JsonSerializer.SerializeToElement(MediaResponse.From(ficha), Json)
        : JsonSerializer.SerializeToElement(MediaListItemResponse.From(ficha), Json);

    [Theory]
    [MemberData(nameof(Respuestas))]
    public void Con_poster_y_sin_backdrop_el_backdrop_viaja_null(string respuesta)
    {
        var json = Serialize(respuesta, FichaWith(T1, null));

        Assert.Equal(MediaImageUrl.For(Id, MediaImageRole.Poster, T1), json.GetProperty("poster_url").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("backdrop_url").ValueKind);
    }

    [Theory]
    [MemberData(nameof(Respuestas))]
    public void Con_backdrop_trae_su_url(string respuesta)
    {
        var json = Serialize(respuesta, FichaWith(null, T2));

        Assert.Equal(MediaImageUrl.For(Id, MediaImageRole.Backdrop, T2), json.GetProperty("backdrop_url").GetString());
    }

    // Tienen que viajar como null, no faltar: el front distingue "sin imagen" de un campo ausente.
    [Theory]
    [MemberData(nameof(Respuestas))]
    public void Sin_imagenes_las_dos_urls_viajan_null(string respuesta)
    {
        var json = Serialize(respuesta, FichaWith(null, null));

        Assert.Equal(JsonValueKind.Null, json.GetProperty("poster_url").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("backdrop_url").ValueKind);
    }

    [Fact]
    public void Una_ficha_recien_creada_no_tiene_urls()
    {
        var json = JsonSerializer.SerializeToElement(MediaResponse.From(Media.FromSource("films/a.mkv", Info)), Json);

        Assert.Equal(JsonValueKind.Null, json.GetProperty("poster_url").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("backdrop_url").ValueKind);
    }
}
