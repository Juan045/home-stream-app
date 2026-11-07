namespace StreamMedia.Application.Images.Interfaces;

// Infrastructure lo implementa con SQLite. No valida: eso es de Application.
public interface IMediaImageRepository
{
    Task<MediaImage?> GetAsync(Guid idMedia, MediaImageRole role, CancellationToken ct = default);

    // Upsert: si ya habia una imagen de ese rol para esa ficha, la reemplaza.
    Task SaveAsync(Guid idMedia, MediaImageRole role, MediaImage image, CancellationToken ct = default);

    // Idempotente: borrar algo que no existe no falla.
    Task DeleteAsync(Guid idMedia, MediaImageRole role, CancellationToken ct = default);
}
