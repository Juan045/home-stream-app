namespace StreamMedia.Application.MediaExceptions;

/// <summary>
/// Represents an exception that occurs when a request is invalid.
/// </summary>
public class InvalidRequestException : Exception
{
    public InvalidRequestException()
    {
    }

    public InvalidRequestException(string detail) : base(detail)
    {
    }
}