using StreamMedia.Application.Images.Interfaces;
using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Application.MediaExceptions;

namespace StreamMedia.Application.Images;

// PUT /api/v1/media/{id}/images/{role}
// El navegador ya comprimio la imagen: aca solo se valida y se guarda tal cual.
public sealed class SaveMediaImageUseCase(
    IMediaRepository mediaRepository,
    IMediaImageRepository images,
    TimeProvider time)
{
    public const int MaxBytes = 1_048_576; // 1 MiB

    public async Task<MediaImage> ExecuteAsync(
        Guid idMedia, MediaImageRole role, byte[] data, CancellationToken ct = default)
    {
        _ = await mediaRepository.GetByIdAsync(idMedia, ct)
            ?? throw new MediaPathException("media_not_found", $"No existe una ficha con id {idMedia}");

        if (data.Length == 0)
            throw new ImageException("invalid_image", "La imagen esta vacia");

        if (data.Length > MaxBytes)
            throw new ImageException("image_too_large", $"La imagen supera {MaxBytes} bytes");

        // El tipo sale de los bytes: el Content-Type del cliente no se usa.
        var contentType = ImageFormat.Detect(data)
            ?? throw new ImageException("invalid_image", "Solo se aceptan imagenes JPEG o WebP");

        var image = new MediaImage(contentType, data, time.GetUtcNow());
        await images.SaveAsync(idMedia, role, image, ct);
        return image;
    }
}
