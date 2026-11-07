using StreamMedia.Application.Images;

// El tipo sale de los bytes: es lo unico que el backend mira de una imagen.
public class ImageFormatTests
{
    public static TheoryData<byte[]> NoSoportados => new()
    {
        { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0] },  // PNG
        { [.. "GIF89a"u8, 0, 0, 0, 0, 0, 0] },                              // GIF
        { [.. """<svg xmlns="http://www.w3.org/2000/svg"></svg>"""u8] },   // SVG
        { [.. "RIFF"u8, 0, 0, 0, 0, .. "WAVE"u8, 0, 0, 0, 0] },             // RIFF que no es WebP
        { [.. "RIFF"u8, 0, 0, 0, 0, .. "WEB"u8] },                          // 11 bytes: no llega a 12
        { [] },
        { [0xFF, 0xD8] },                                                   // firma JPEG cortada
    };

    [Fact]
    public void Un_jpeg_es_image_jpeg()
        => Assert.Equal("image/jpeg", ImageFormat.Detect([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0]));

    [Fact]
    public void Un_webp_es_image_webp()
        => Assert.Equal("image/webp", ImageFormat.Detect([.. "RIFF"u8, 1, 2, 3, 4, .. "WEBP"u8, .. "VP8 "u8, 0, 0]));

    [Theory]
    [MemberData(nameof(NoSoportados))]
    public void Lo_que_no_es_jpeg_ni_webp_da_null(byte[] data)
        => Assert.Null(ImageFormat.Detect(data));
}
