namespace StreamMedia.Application.Streaming.Interfaces;

public interface IFfmpegRunner
{
    /// <summary>
    /// Ejecuta FFmpeg hasta el final. onProgress recibe los segundos de media ya procesados.
    /// Cancelar el token mata el proceso y lanza OperationCanceledException.
    /// Lanza FfmpegException si termina con codigo distinto de cero.
    /// </summary>
    Task RunAsync(IReadOnlyList<string> args, Action<double>? onProgress = null, CancellationToken ct = default);
}