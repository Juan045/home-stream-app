using StreamMedia.Application.Media.Interfaces;

namespace StreamMedia.Application.Media;

// Traduccion de GET /api/v1/media de Python (media_service.page).
// Los dos datos viajan juntos a proposito: el total es el "6 titles" del
// encabezado de cada seccion, y pedirlo aparte con otros filtros lo
// desincroniza de la pagina.
public sealed record MediaPage(IReadOnlyList<Domain.Entities.Media> Items, int Total);

public sealed class ListMediaUseCase(IMediaRepository mediaRepository)
{
    public async Task<MediaPage> ExecuteAsync(MediaListQuery query, CancellationToken ct = default)
    {
        var items = await mediaRepository.ListAsync(query, ct);
        var total = await mediaRepository.CountAsync(query, ct);
        return new MediaPage(items, total);
    }
}