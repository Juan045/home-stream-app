namespace StreamMedia.Application.MediaExceptions;

/// <summary>Imagen de una ficha rechazada o inexistente.</summary>
/// <param name="code">Codigo que ve el cliente: invalid_image, image_too_large o image_not_found.</param>
/// <param name="message">Detalle legible.</param>
public class ImageException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
