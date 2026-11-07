namespace StreamMedia.Application.Images;

// Los bytes tal cual los subio el navegador; UpdatedAt versiona la URL (?v=).
public sealed record MediaImage(string ContentType, byte[] Data, DateTimeOffset UpdatedAt);
