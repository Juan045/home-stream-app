using System.Text.Json;
using Microsoft.AspNetCore.Http;
using StreamMedia.Application.Images;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.WebApi.Contracts;
using StreamMedia.WebApi.Errors;

// URL, respuesta del PUT y errores de las imagenes de una ficha. Sin servidor: no cubre routing ni cabeceras.
public class MediaImagesContractTests
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    static readonly Guid Id = Guid.NewGuid();
    static readonly DateTimeOffset At = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void La_url_del_poster_lleva_el_instante_en_milisegundos()
        => Assert.Equal(
            $"/api/v1/media/{Id}/images/poster?v={At.ToUnixTimeMilliseconds()}",
            MediaImageUrl.For(Id, MediaImageRole.Poster, At));

    [Fact]
    public void La_url_del_backdrop_usa_el_segmento_backdrop()
        => Assert.Equal(
            $"/api/v1/media/{Id}/images/backdrop?v={At.ToUnixTimeMilliseconds()}",
            MediaImageUrl.For(Id, MediaImageRole.Backdrop, At));

    // El ?v= es lo que rompe el cache immutable: cambia solo si cambia la imagen.
    [Fact]
    public void La_url_cambia_con_el_instante_y_no_sin_el()
    {
        Assert.NotEqual(
            MediaImageUrl.For(Id, MediaImageRole.Poster, At),
            MediaImageUrl.For(Id, MediaImageRole.Poster, At.AddMilliseconds(1)));
        Assert.Equal(
            MediaImageUrl.For(Id, MediaImageRole.Poster, At),
            MediaImageUrl.For(Id, MediaImageRole.Poster, At));
    }

    [Fact]
    public void La_respuesta_del_put_tiene_solo_url()
    {
        var json = JsonSerializer.SerializeToElement(new MediaImageResponse("/x"), Json);

        var property = Assert.Single(json.EnumerateObject());
        Assert.Equal("url", property.Name);
        Assert.Equal("/x", property.Value.GetString());
    }

    [Theory]
    [InlineData("image_not_found", StatusCodes.Status404NotFound)]
    [InlineData("invalid_image", StatusCodes.Status422UnprocessableEntity)]
    [InlineData("image_too_large", StatusCodes.Status413PayloadTooLarge)]
    public async Task Cada_error_de_imagen_tiene_su_status_y_su_codigo(string code, int status)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var handled = await new ApiExceptionHandler().TryHandleAsync(
            context, new ImageException(code, "detalle"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(status, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
        Assert.Equal(code, body.GetProperty("error").GetString());
        Assert.Equal("detalle", body.GetProperty("detail").GetString());
    }
}
