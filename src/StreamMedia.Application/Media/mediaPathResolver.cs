using StreamMedia.Application.MediaExceptions;

namespace StreamMedia.Application.Media;

/* public sealed class MediaPathException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
} */

public sealed class MediaPathResolver(string root)
{
    private static readonly string[] Extensions = [".mp4", ".mkv"];
    private readonly string _root = Path.GetFullPath(root);

    public string Resolve(string relativePath)
    {
        // Antes del Combine: Path.Combine("/media", "/etc/passwd") devuelve "/etc/passwd".
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new MediaPathException("invalid_path", "La ruta debe ser relativa a MEDIA_ROOT.");

        var full = Path.GetFullPath(Path.Combine(_root, relativePath)); // resuelve los ".."

        var rel = Path.GetRelativePath(_root, full);
        if (rel == ".." || rel.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(rel))
            throw new MediaPathException("outside_media_root", "La ruta queda fuera de MEDIA_ROOT.");

        if (!Extensions.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase))
            throw new MediaPathException("unsupported_extension", "Solo se admiten .mp4 y .mkv.");

        // ponytail: no resuelve symlinks (Python resolve() sí); MEDIA_ROOT va :ro, agregar si hay links.
        if (!File.Exists(full))
            throw new MediaPathException("file_not_found", $"El archivo no existe: {relativePath}");

        return full;
    }
}