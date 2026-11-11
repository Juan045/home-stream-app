namespace StreamMedia.Domain.Entities;

// Pending va primero: default(ArtifactState) es 0, asi que un Artifact
// nuevo arranca Pending aunque nadie lo inicialice.
public enum ArtifactState { Pending, Building, Ready, Failed }

// Estado de una pista generada (el video, una pista de audio, un subtitulo).
// Se muta in-place mientras FFmpeg corre: el polling ve el avance sin tocar disco.
public sealed class Artifact
{
    public ArtifactState State { get; set; } = ArtifactState.Pending;
    public string? Error { get; set; }
    public double SecondsDone { get; set; }

    // Ya consiguio slot de FFmpeg. No se persiste: al recargar no hay nada en cola.
    public bool Started { get; set; }

    // Building incluye la espera del semaforo. No es otro estado: el enum va al manifest y
    // BuildingCount, BuildingAssetIds y Asset.Status cuentan con Building.
    public bool IsQueued => State == ArtifactState.Building && !Started;

    // Su build anterior se corto: el manifest decia building y el disco no esta completo.
    // Acompana a un Pending y solo informa (el admin lo muestra). No se persiste: se deduce al leer.
    public bool Interrupted { get; set; }
}

// Un .vtt planeado para una pista: el nombre del archivo y el estado de su extraccion.
public sealed class SubtitleArtifact(string fileName)
{
    public string FileName { get; } = fileName;
    public Artifact Artifact { get; } = new();
}