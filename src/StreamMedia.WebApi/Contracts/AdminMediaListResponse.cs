namespace StreamMedia.WebApi.Contracts;

/// <summary>
/// Una pagina del catalogo con la ficha completa en cada fila, para la Biblioteca del admin.
/// La galeria sigue usando <see cref="MediaListResponse"/>, que trae solo lo que dibuja una tarjeta.
/// </summary>
/// <param name="Items">Las fichas de la pagina.</param>
/// <param name="Total">Fichas que matchean los filtros, sin paginar.</param>
/// <param name="Limit">Tamano de pagina aplicado.</param>
/// <param name="Offset">Desplazamiento aplicado.</param>
public record AdminMediaListResponse(
    IReadOnlyList<MediaResponse> Items,
    int Total,
    int Limit,
    int Offset);
