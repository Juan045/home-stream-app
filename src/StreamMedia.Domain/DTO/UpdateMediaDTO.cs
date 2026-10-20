using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace StreamMedia.Domain.DTO;

/// <summary>
/// Campos editoriales de una ficha. Ausente = no tocar; en los cuatro nullables,
/// <c>null</c> explicito = vaciar. Los derivados del archivo se rechazan.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record UpdateMediaRequest(
    string? Title,
    [param: AllowedValues("film", "series", "documentary", null)] string? Kind,
    [param: AllowedValues("remux", "transcode", null)] string? Strategy,
    bool? InList,
    IReadOnlyList<int>? IgnoredAudio,
    IReadOnlyList<int>? IgnoredSubtitles,
    Optional<int?> Year,
    Optional<string?> Synopsis,
    Optional<IReadOnlyList<string>?> Genres,
    Optional<string?> Notes);