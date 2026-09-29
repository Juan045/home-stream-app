namespace StreamMedia.Application.MediaExceptions;

public sealed class MediaPathException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}