using StreamMedia.Application.Encoder;

namespace StreamMedia.WebApi.Contracts;

// Lo que ve la ficha: el mismo estado que /sessions pero sin sesion ni reproduccion.
// Los nombres salen en snake_case por la politica global de JSON (Program.cs).
public sealed record EncodeResponse(
    Guid IdMedia, string? AssetId, string State, double Progress, string? VideoCodec, string? Error)
{
    public static EncodeResponse From(EncodeStatus status) => new(
        status.IdMedia, status.AssetId, status.State, status.Progress, status.VideoCodec, status.Error);
}
