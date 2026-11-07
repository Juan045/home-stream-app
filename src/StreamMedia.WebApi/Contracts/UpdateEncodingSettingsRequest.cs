namespace StreamMedia.WebApi.Contracts;

// Nullable a proposito: con string, [ApiController] contestaria su propio 400 antes de llegar
// al use case, y un campo ausente no responderia en el formato de error del proyecto.
public sealed record UpdateEncodingSettingsRequest(string? LiveCodec, string? ArchiveCodec);
