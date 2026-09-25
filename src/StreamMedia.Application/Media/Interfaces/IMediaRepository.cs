using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Media.Interfaces;

// Infrastructure va a implementar esto con SQLite.
// Application y Domain no saben que existe SQLite.
public interface IMediaRepository
{
    Task<Domain.Entities.Media?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Domain.Entities.Media?> FindByPathAsync(string filePath, CancellationToken ct = default);
    Task AddAsync(Domain.Entities.Media media, CancellationToken ct = default);
}
