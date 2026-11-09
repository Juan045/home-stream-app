using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Artifacts;

// Que mostrar de un asset que no esta en memoria. Un solo proceso: sin asset en memoria no hay
// FFmpeg corriendo, asi que el "building" de un manifest es de un build que se corto.
// Solo informa: reabrir el asset lo decide LoadAsync, y /encode sigue leyendo el manifest (TODO #5).
public static class ColdArtifactState
{
    // Video y audio. null = la pista no esta en el manifest. Func: Ready y Failed no tocan el disco.
    public static string Resolve(ArtifactState? inManifest, Func<bool> playlistComplete)
    {
        if (inManifest == ArtifactState.Ready)  
            return "ready";
            
        if (inManifest == ArtifactState.Failed) 
            return "failed";

        if (playlistComplete()) return "ready";

        return inManifest == ArtifactState.Building ? "interrupted" : "pending";
    }

    // Mismo orden que AssetBuilder.SubtitleState. Un subtitulo cortado no deja rastro: da "pending".
    public static string ResolveSubtitle(AssetManifest manifest, int index, Func<string, bool> vttExists)
    {
        if (manifest.Subtitles.TryGetValue(index, out var vtt) && vttExists(vtt))
            return "ready";

        return manifest.SubtitlesFailed.Contains(index) ? "failed" : "pending";
    }
}
