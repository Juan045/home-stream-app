namespace StreamMedia.Application.MediaExceptions;

// Es el 400 invalid_media de Python
public sealed class InvalidMediaException(string message) : Exception(message);