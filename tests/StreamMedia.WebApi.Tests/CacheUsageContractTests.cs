using System.Text.Json;
using StreamMedia.Application.Cache;
using StreamMedia.WebApi.Contracts;

// El contrato que consume la tarjeta del cache. Sin servidor: misma politica de nombres de Program.cs.
public class CacheUsageContractTests
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // Valores por encima de int.MaxValue: el default de 50 GiB no entra en 32 bits.
    [Fact]
    public void La_respuesta_tiene_el_uso_y_el_limite_en_snake_case()
    {
        var json = JsonSerializer.SerializeToElement(
            CacheUsageResponse.From(new CacheUsage(UsedBytes: 3_000_000_000, MaxBytes: 53_687_091_200)),
            Json);

        Assert.Equal(2, json.EnumerateObject().Count());
        Assert.Equal(3_000_000_000, json.GetProperty("used_bytes").GetInt64());
        Assert.Equal(53_687_091_200, json.GetProperty("max_bytes").GetInt64());
    }
}
