using StreamMedia.Application.Encoder;
using StreamMedia.Domain.Entities;

namespace StreamMedia.WebApi.Contracts;

// Las opciones viajan con el ajuste: la pagina no tiene una lista propia de codecs.
// Los nombres salen en snake_case por la politica global de JSON (Program.cs).
public sealed record EncodingSettingsResponse(
    string LiveCodec,
    string ArchiveCodec,
    IReadOnlyList<string> LiveCodecs,
    IReadOnlyList<string> ArchiveCodecs,
    string DefaultLiveCodec,
    string DefaultArchiveCodec)
{
    public static EncodingSettingsResponse From(EncodingSettings settings) => new(
        settings.LiveCodec,
        settings.ArchiveCodec,
        [.. TranscodeProfiles.LiveCodecs],
        [.. TranscodeProfiles.ArchiveCodecs],
        EncodingSettings.Default.LiveCodec,
        EncodingSettings.Default.ArchiveCodec);
}
