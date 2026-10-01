namespace StreamMedia.Application.MediaExceptions;

/* public sealed class MediaPathException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
} */
public class MediaPathException : Exception
{
    public string Code { get; }

    public MediaPathException(string code, string message) : base(message)
    {
        Code = code;
    }
}