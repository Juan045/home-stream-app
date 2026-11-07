namespace StreamMedia.Application.Artifacts;

/// <summary>Estado de un artefacto, en el vocabulario que ve el panel de admin.</summary>
/// <param name="Index">Indice de la pista; null en el video.</param>
/// <param name="State">idle, pending, building, ready, failed o interrupted.</param>
/// <param name="Progress">Entre 0 y 1; null si el artefacto no reporta avance (subtitulos sin terminar).</param>
/// <param name="Queued">Esta en building, pero todavia espera un slot de FFmpeg.</param>
/// <param name="Error">El motivo, si fallo.</param>
public sealed record ArtifactStatus(int? Index, string State, double? Progress, bool Queued, string? Error);
