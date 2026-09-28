namespace StreamMedia.Infrastructure.Ffmpeg;

public sealed class FfmpegOptions
{
    public const string SectionName = "Ffmpeg";

    /// <summary>Binario de ffmpeg. "ffmpeg" lo busca en el PATH.</summary>
    public string Path { get; init; } = "ffmpeg";

    /// <summary>Binario de ffprobe. "ffprobe" lo busca en el PATH.</summary>
    public string ProbePath { get; init; } = "ffprobe";

    /// <summary>Procesos ffmpeg simultáneos (todavía sin uso).</summary>
    public int MaxConcurrent { get; init; } = 2;
}