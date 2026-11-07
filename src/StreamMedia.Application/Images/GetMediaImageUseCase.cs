using StreamMedia.Application.Images.Interfaces;
using StreamMedia.Application.MediaExceptions;

namespace StreamMedia.Application.Images;

// GET /api/v1/media/{id}/images/{role}
// No consulta la ficha: sin ficha tampoco hay imagen (FK), y es una lectura menos.
public sealed class GetMediaImageUseCase(IMediaImageRepository images)
{
    public async Task<MediaImage> ExecuteAsync(
        Guid idMedia, MediaImageRole role, CancellationToken ct = default)
        => await images.GetAsync(idMedia, role, ct)
            ?? throw new ImageException("image_not_found", $"La ficha {idMedia} no tiene imagen {role.ToString().ToLowerInvariant()}");
}
