namespace StreamMedia.Application.MediaExceptions;

/// <summary>La sesión o el asset pedido no existe (404).</summary>
/// <param name="code">Código de error que ve el cliente: session_not_found o asset_not_found.</param>
/// <param name="message">Detalle legible.</param>
public sealed class StreamNotFoundException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}