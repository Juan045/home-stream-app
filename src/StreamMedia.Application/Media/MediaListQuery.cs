namespace StreamMedia.Application.Media;

// Traduccion de los parametros de GET /api/v1/media de Python (media_service.page).
// Un solo tipo en vez de repetir q/kind/inList/sort/limit/offset en la firma
// del use case, del repositorio y de sus dos metodos (list + count).
public sealed record MediaListQuery(
    string? Q = null,
    string? Kind = null,
    bool? InList = null,
    string Sort = "title",
    int Limit = 10,
    int Offset = 0);