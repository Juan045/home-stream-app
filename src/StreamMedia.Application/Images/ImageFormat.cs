namespace StreamMedia.Application.Images;

// El tipo sale de los primeros bytes, nunca del Content-Type del cliente:
// asi no se guarda ni se sirve algo que el navegador interprete distinto (SVG con script).
public static class ImageFormat
{
    public static string? Detect(ReadOnlySpan<byte> data)
    {
        if (data is [0xFF, 0xD8, 0xFF, ..])
            return "image/jpeg";

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";

        return null;
    }
}
