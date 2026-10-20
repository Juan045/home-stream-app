using System.Diagnostics;
using System.Text.Json;
using StreamMedia.Infrastructure.Ffmpeg;
using StreamMedia.Application.MediaExceptions;

namespace StreamMedia.Infrastructure.Media;

public sealed class FfprobeRunner(FfmpegOptions options)
{
    public async Task<JsonDocument> ProbeAsync(string path, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(options.ProbePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-v", "error", "-print_format", "json", "-show_streams", "-show_format", path })
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)!;

        // Se leen ANTES de esperar la salida: si no, un pipe lleno bloquea a ffprobe.
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);

        try { await proc.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { proc.Kill(entireProcessTree: true); throw; }

        if (proc.ExitCode != 0)
            throw new InvalidMediaException($"ffprobe terminó con código {proc.ExitCode}: {await stderr}");

        return JsonDocument.Parse(await stdout);
    }
}