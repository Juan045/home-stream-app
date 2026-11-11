namespace StreamMedia.Application.MediaExceptions;

/// <summary>El video del asset fallo y no se reintenta: no se abre ni se sirve, aunque tenga segmentos (409).</summary>
public sealed class VideoFailedException(string assetId, string? error)
    : Exception($"El video de {assetId} fallo y no se reintenta: {error}")
{
    public string Code => "video_failed";
}
