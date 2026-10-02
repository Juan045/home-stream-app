namespace StreamMedia.Domain.DTO;

// Nombres de lo que produce FFmpeg y del layout en disco.
// Los usan el TranscodeCommandBuilder, el AssetStore y (paso 7) las playlists.
public static class HlsLayout
{
    public const string ManifestName = "manifest.json";
    public const string AccessMarker = ".last-access";

    public const string VideoDir = "video";
    public const string AudioDir = "audio";
    public const string SubsDir = "subs";

    public const string InitName = "init.mp4";
    public const string SegmentTemplate = "seg-%05d.m4s"; // forma printf, para -hls_segment_filename
    public const string SegmentGlob = "seg-*.m4s";        // para contar segmentos en disco
    public const string InternalPlaylist = "internal.m3u8";
}