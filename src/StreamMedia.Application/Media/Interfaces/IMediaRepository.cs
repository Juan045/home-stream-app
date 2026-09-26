using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Media.Interfaces;

// Infrastructure va a implementar esto con SQLite.
// Application y Domain no saben que existe SQLite.
public interface IMediaRepository
{
    Task<Domain.Entities.Media?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Domain.Entities.Media?> FindByPathAsync(string filePath, CancellationToken ct = default);
    Task AddAsync(Domain.Entities.Media media, CancellationToken ct = default);

    // Los mismos filtros para las dos: la pagina y el total tienen que
    // contar siempre lo mismo, o la UI muestra "pagina 1 de 3" con 2 items.
    Task<IReadOnlyList<Domain.Entities.Media>> ListAsync(MediaListQuery query, CancellationToken ct = default);

    Task<int> CountAsync(MediaListQuery query, CancellationToken ct = default);
}
