using StreamMedia.Application.Media.Interfaces;

namespace StreamMedia.Application.Media;

// GET /api/v1/media/{id_media}
// Sin logica propia por ahora: es el lugar donde entraria si alguna vez la
// necesita (permisos, cache), sin que el Controller tenga que saber.
public sealed class GetMediaByIdUseCase(IMediaRepository mediaRepository)
{
    public Task<Domain.Entities.Media?> ExecuteAsync(Guid id, CancellationToken ct = default)
        => mediaRepository.GetByIdAsync(id, ct);
}