namespace StreamMedia.Application.MediaExceptions;

/// <summary>La playlist de un asset en construccion todavia no se escribio (503, reintentar).</summary>
public sealed class PlaylistNotReadyException(string message) : Exception(message);