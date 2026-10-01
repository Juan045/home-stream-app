namespace StreamMedia.Application.MediaExceptions;

public class FfmpegException : Exception
{
    public int ExitCode { get; }
    public string Stderr { get; }

    public FfmpegException(int exitCode, string stderr) : base(stderr)
    {
        ExitCode = exitCode;
        Stderr = stderr;
    }
}