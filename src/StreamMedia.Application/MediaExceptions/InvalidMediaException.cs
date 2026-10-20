namespace StreamMedia.Application.MediaExceptions;

/// <summary>
/// Represents an exception that occurs when a media file is invalid.
/// </summary>
public class InvalidMediaException : Exception
{
    public InvalidMediaException()
    {
    }

    public InvalidMediaException(string message) : base(message)
    {
    }
}