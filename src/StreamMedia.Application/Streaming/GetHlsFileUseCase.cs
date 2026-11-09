using StreamMedia.Application.Cache;
using StreamMedia.Application.Cache.Interfaces;
using StreamMedia.Application.MediaExceptions;

namespace StreamMedia.Application.Streaming;

/// <summary>
/// Resuelve los archivos que las playlists referencian (init.mp4, segmentos y subtítulos)
/// a una ruta física. No abre el archivo: de servirlo se encarga el controller.
/// </summary>
public sealed class GetHlsFileUseCase(IAssetRepository manifests, IHlsOutput hls)
{
    // Solo estas extensiones salen al cliente. Es lo que deja afuera al internal.m3u8 de FFmpeg,
    // que nunca debe servirse: las playlists que ve el navegador se calculan.
    static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4",
        [".m4s"] = "video/iso.segment",
        [".vtt"] = "text/vtt",
    };

    /// <summary>Archivo de la carpeta de video: init.mp4 o un segmento.</summary>
    public (string FullPath, string ContentType) Video(string assetId, string file)
        => Resolve(assetId, file, OutputFolder.Video);

    /// <summary>Archivo de la carpeta de una pista de audio: init.mp4 o un segmento.</summary>
    public (string FullPath, string ContentType) Audio(string assetId, int track, string file)
        => Resolve(assetId, file, OutputFolder.Audio(track));

    /// <summary>Subtítulo WebVTT ya extraído.</summary>
    public (string FullPath, string ContentType) Subtitle(string assetId, string file)
        => Resolve(assetId, file, OutputFolder.Subs);

    (string FullPath, string ContentType) Resolve(string assetId, string file, OutputFolder folder)
    {
        if (!IsPlainName(assetId) || !manifests.Exists(assetId))
            throw new StreamNotFoundException("asset_not_found", $"El asset {assetId} no existe");

        if (!IsPlainName(file) || !ContentTypes.TryGetValue(Path.GetExtension(file), out var contentType))
            throw FileNotFound(assetId, file);

        // La ruta sale absoluta: PhysicalFile la exige y Cache:Dir puede ser relativo.
        var fullPath = hls.FindFile(assetId, folder, file) ?? throw FileNotFound(assetId, file);

        return (fullPath, contentType);
    }

    // Un nombre simple: sin separadores ni "..". Cierra el path traversal sin depender
    // de cómo lo parsee el router.
    static bool IsPlainName(string name)
        => name.Length > 0 && name == Path.GetFileName(name) && name is not ("." or "..");

    static StreamNotFoundException FileNotFound(string assetId, string file)
        => new("hls_file_not_found", $"El asset {assetId} no tiene el archivo {file}");
}