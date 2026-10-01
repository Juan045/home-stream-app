using StreamMedia.Application.MediaExceptions;

namespace StreamMedia.Application.Streaming;

// POST /api/v1/stream
// Paso 0: solo valida la ruta. Todavia no abre nada.
public sealed class StartStreamUseCase
{
    public Task ExecuteAsync(string filePath, CancellationToken ct = default)
    {
        // Regla 1 de validate_path (helpers.py): la ruta de /stream llega ABSOLUTA.
        if (!Path.IsPathFullyQualified(filePath))
            throw new MediaPathException("invalid_path", "La ruta debe ser absoluta");

        return Task.CompletedTask;
    }
}