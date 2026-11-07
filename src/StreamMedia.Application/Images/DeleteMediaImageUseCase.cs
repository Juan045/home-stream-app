using StreamMedia.Application.Images.Interfaces;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.MediaExceptions;

namespace StreamMedia.Application.Images;

// DELETE /api/v1/media/{id}/images/{role}
// Idempotente si la ficha existe: borrar una imagen que no esta no es un error.
public sealed class DeleteMediaImageUseCase(
    IMediaRepository mediaRepository,
    IMediaImageRepository images)
{
    public async Task ExecuteAsync(Guid idMedia, MediaImageRole role, CancellationToken ct = default)
    {
        _ = await mediaRepository.GetByIdAsync(idMedia, ct)
            ?? throw new MediaPathException("media_not_found", $"No existe una ficha con id {idMedia}");

        await images.DeleteAsync(idMedia, role, ct);
    }
}
