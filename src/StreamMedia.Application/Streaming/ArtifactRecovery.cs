using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Streaming;

// Que estado tiene un artefacto al sacarlo del cache: cruza lo que dice el manifest con lo que hay en disco.
// Unica regla: la usan abrir (AssetBuilder.OpenAsync) y mirar sin abrir (AssetBuilder.PeekAsync).
// Pura: el disco entra como funcion y solo se consulta si hace falta.
public static class ArtifactRecovery
{
    // inManifest null = el artefacto no figura en el manifest (o no hay manifest).
    public static void Recover(Artifact artifact, ArtifactState? inManifest, string? error, Func<bool> completeOnDisk)
    {
        // Terminal y sin mirar el disco: si FFmpeg termino con error, los segmentos pueden estar cortados
        // o mal formados aunque la playlist tenga ENDLIST. Se sale con "descartar y reintentar" (TODO.md #21).
        if (inManifest == ArtifactState.Failed)
        {
            artifact.State = ArtifactState.Failed;
            artifact.Error = error;
            return;
        }

        // El disco manda sobre ready/building/pending: un manifest puede quedar viejo tras un corte.
        if (completeOnDisk())
        {
            artifact.State = ArtifactState.Ready;
            return;
        }

        artifact.State = ArtifactState.Pending;
        artifact.Interrupted = inManifest == ArtifactState.Building;
    }
}
