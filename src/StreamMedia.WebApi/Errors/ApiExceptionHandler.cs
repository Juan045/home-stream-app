using Microsoft.AspNetCore.Diagnostics;
using StreamMedia.Application.MediaExceptions;

namespace StreamMedia.WebApi.Errors;

/// <summary>Traduce las excepciones de Application al formato de error { error, detail }.</summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // El orden importa: el compilador rechaza un caso que otro anterior ya cubre.
        var (status, error) = exception switch
        {
            MediaPathException { Code: "file_not_found" or "media_not_found" } e => (StatusCodes.Status404NotFound, e.Code),
            MediaPathException e => (StatusCodes.Status400BadRequest, e.Code),
            InvalidMediaException or FfmpegException => (StatusCodes.Status400BadRequest, "invalid_media"),
            CapacityException { Code: "too_many_jobs" } e => (StatusCodes.Status503ServiceUnavailable, e.Code),
            CapacityException e => (StatusCodes.Status507InsufficientStorage, e.Code),
            AssetAlreadyEncodedException e => (StatusCodes.Status409Conflict, e.Code),
            InvalidRequestException => (StatusCodes.Status422UnprocessableEntity, "invalid_request"),
            StreamNotFoundException e => (StatusCodes.Status404NotFound, e.Code),
            PlaylistNotReadyException => (StatusCodes.Status503ServiceUnavailable, "playlist_not_ready"),
            _ => (0, "")
        };

        // No es una excepción nuestra: devolver false deja que ASP.NET responda el 500 de siempre.
        if (status == 0)
            return false;

        // Un 404 haria que hls.js deje de pedir la playlist; el 503 con Retry-After lo hace reintentar.
        if (exception is PlaylistNotReadyException)
            httpContext.Response.Headers.RetryAfter = "2";

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            new { error, detail = exception.Message }, cancellationToken);
        return true;
    }
}