using System.Collections.Concurrent;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Domain.Entities;

namespace StreamMedia.Infrastructure.Media;

// Implementacion temporal de IMediaRepository, solo para probar el flujo
// de punta a punta sin esperar a tener SQLite mapeado.
// Se registra como Singleton en Program.cs para que los datos sobrevivan
// entre requests mientras corre el proceso (se pierden al reiniciar).

public sealed class InMemoryMediaRepository : IMediaRepository
{

    // ConcurrentDictionary porque ASP.NET puede atender requests en paralelo
    // y un Dictionary comun no es thread-safe.
    private readonly ConcurrentDictionary<Guid, Domain.Entities.Media> _byId = new();

    public Task<Domain.Entities.Media?> FindByPathAsync(string relativePath, CancellationToken ct)
    {
        // This implementation assumes you have a way to find media by path
        // For now, we'll just return null as a placeholder
        var found = _byId.Values.FirstOrDefault(m => m.FilePath == relativePath);
        return Task.FromResult(found);
    }

    public Task<Domain.Entities.Media?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        _byId.TryGetValue(id, out var media);
        return Task.FromResult(media);
    }

    public Task AddAsync(Domain.Entities.Media media, CancellationToken ct)
    {
        _byId[media.Id] = media;
        return Task.CompletedTask;
    }
}