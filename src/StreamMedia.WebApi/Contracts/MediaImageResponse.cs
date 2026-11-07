namespace StreamMedia.WebApi.Contracts;

/// <summary>Respuesta del PUT de una imagen: la URL versionada para pedirla.</summary>
public sealed record MediaImageResponse(string Url);
