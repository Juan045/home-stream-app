using System.Diagnostics;
using StreamMedia.Application.Streaming.Interfaces;
using StreamMedia.Application.MediaExceptions;

namespace StreamMedia.Infrastructure.Ffmpeg;

public class FfmpegRunner(FfmpegOptions options) : IFfmpegRunner
{
    // Cuantas lineas de stderr se guardan para el reporte de error.
    const int StderrTailLines = 40;

        public async Task RunAsync(IReadOnlyList<string> args, Action<double>? onProgress = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(options.Path)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)!;
        // Va despues del proceso: se libera antes que el, asi el callback nunca toca un proceso ya liberado.
        using var onCancel = ct.Register(() => Kill(proc));

        // Nada de stdin: el comando de subtitulos no lleva -nostdin y ffmpeg podria quedarse esperando.
        proc.StandardInput.Close();

        // stderr se drena en paralelo con stdout: si cualquiera de los dos pipes se llena, ffmpeg se bloquea.
        var tail = new Queue<string>();
        var stderr = DrainAsync(proc.StandardError, tail);

        double? last = null;
        while (await proc.StandardOutput.ReadLineAsync() is { } line)
        {
            var seconds = ParseProgressSeconds(line);
            // ffmpeg emite out_time_us y out_time_ms con el mismo valor en cada bloque:
            // sin este filtro cada avance se reportaria dos veces.
            if (seconds is null || seconds == last) continue;
            last = seconds;
            onProgress?.Invoke(seconds.Value);
        }

        await stderr;
        await proc.WaitForExitAsync();

        ct.ThrowIfCancellationRequested();
        if (proc.ExitCode != 0)
            throw new FfmpegException(proc.ExitCode, string.Join('\n', tail));
    }

    /// <summary>Segundos procesados de una linea de -progress, o null si la linea no reporta tiempo.</summary>
    public static double? ParseProgressSeconds(string line)
    {
        var eq = line.IndexOf('=');
        if (eq < 0) return null;

        var key = line[..eq].Trim();
        if (key is not ("out_time_us" or "out_time_ms")) return null;

        // Ambos vienen en microsegundos. Al inicio puede venir "N/A" y algunas versiones emiten negativos.
        return long.TryParse(line[(eq + 1)..].Trim(), out var micros) && micros >= 0
            ? micros / 1_000_000.0
            : null;
    }

    static async Task DrainAsync(StreamReader reader, Queue<string> tail)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (tail.Count == StderrTailLines) tail.Dequeue();
            tail.Enqueue(line);
        }
    }

    static void Kill(Process proc)
    {
        try { proc.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { } // ya termino (o ya se libero)
    }

}