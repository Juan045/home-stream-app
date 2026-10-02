namespace StreamMedia.Domain.DTO;

public sealed record AssetPaths(string Root)
{
    public string Manifest => Path.Combine(Root, HlsLayout.ManifestName);
    public string AccessMarker => Path.Combine(Root, HlsLayout.AccessMarker);
    public string Video => Path.Combine(Root, HlsLayout.VideoDir);
    public string Subs => Path.Combine(Root, HlsLayout.SubsDir);
    public string AudioRoot => Path.Combine(Root, HlsLayout.AudioDir);
    public string Audio(int track) => Path.Combine(AudioRoot, track.ToString());
}