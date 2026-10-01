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
}

// Un .vtt planeado para una pista: el nombre del archivo y el estado de su extraccion.
public sealed class SubtitleArtifact(string fileName)
{
    public string FileName { get; } = fileName;
    public Artifact Artifact { get; } = new();
}