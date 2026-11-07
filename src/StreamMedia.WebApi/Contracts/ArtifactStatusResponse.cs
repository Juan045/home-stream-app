using StreamMedia.Application.Artifacts;

namespace StreamMedia.WebApi.Contracts;

/// <summary>Estado de un artefacto. Sale en snake_case por la politica global de JSON (Program.cs).</summary>
public sealed record ArtifactStatusResponse(int? Index, string State, double? Progress, bool Queued, string? Error)
{
    public static ArtifactStatusResponse From(ArtifactStatus status) => new(
        status.Index, status.State, status.Progress, status.Queued, status.Error);
}
