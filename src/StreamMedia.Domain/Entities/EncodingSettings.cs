namespace StreamMedia.Domain.Entities;

// Los codecs que eligio el admin. Sin nada guardado rige Default: el comportamiento de siempre.
public sealed record EncodingSettings(string LiveCodec, string ArchiveCodec)
{
    public static readonly EncodingSettings Default = new("h264", "av1");
}
