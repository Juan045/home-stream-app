namespace StreamMedia.Application.Cache;

// Que carpeta del asset: la clave con la que se pide la salida de FFmpeg en vez de una ruta.
// Track solo tiene sentido con Audio; el constructor privado evita combinaciones sueltas.
public readonly record struct OutputFolder
{
    public OutputFolderKind Kind { get; }
    public int Track { get; }

    OutputFolder(OutputFolderKind kind, int track) => (Kind, Track) = (kind, track);

    public static readonly OutputFolder Video = new(OutputFolderKind.Video, 0);
    public static readonly OutputFolder Subs = new(OutputFolderKind.Subs, 0);

    public static OutputFolder Audio(int track) => new(OutputFolderKind.Audio, track);
}
