using Microsoft.AspNetCore.Mvc;
using StreamMedia.Application.Images;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.WebApi.Contracts;

namespace StreamMedia.WebApi.Controllers
{
    // Poster y backdrop de una ficha. Otro rol no matchea la ruta: 404 de routing.
    [ApiController]
    [Route("api/v1/media/{id:guid}/images/{role:regex(^(poster|backdrop)$)}")]
    public class MediaImagesController(
        SaveMediaImageUseCase saveMediaImageUseCase,
        GetMediaImageUseCase getMediaImageUseCase,
        DeleteMediaImageUseCase deleteMediaImageUseCase) : ControllerBase
    {
        // El body son los bytes crudos del archivo, sin JSON ni multipart.
        [HttpPut]
        public async Task<IActionResult> Put(Guid id, MediaImageRole role, CancellationToken ct)
        {
            var data = await ReadBodyAsync(ct);
            var image = await saveMediaImageUseCase.ExecuteAsync(id, role, data, ct);
            return Ok(new MediaImageResponse(MediaImageUrl.For(id, role, image.UpdatedAt)));
        }

        [HttpGet]
        public async Task<IActionResult> Get(Guid id, MediaImageRole role, CancellationToken ct)
        {
            var image = await getMediaImageUseCase.ExecuteAsync(id, role, ct);

            Response.Headers.XContentTypeOptions = "nosniff";
            // Con ?v= la URL cambia en cada subida; sin v hay que revalidar siempre.
            Response.Headers.CacheControl = Request.Query.ContainsKey("v")
                ? "public, max-age=31536000, immutable"
                : "no-cache";

            return File(image.Data, image.ContentType);
        }

        [HttpDelete]
        public async Task<IActionResult> Delete(Guid id, MediaImageRole role, CancellationToken ct)
        {
            await deleteMediaImageUseCase.ExecuteAsync(id, role, ct);
            return NoContent();
        }

        // Lee como maximo MaxBytes + 1: si llega a eso ya se sabe que es demasiado grande
        // y no se sigue leyendo un body arbitrario a memoria.
        private async Task<byte[]> ReadBodyAsync(CancellationToken ct)
        {
            const int max = SaveMediaImageUseCase.MaxBytes;

            if (Request.ContentLength > max)
                throw TooLarge();

            var buffer = new byte[max + 1];
            var read = await Request.Body.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);

            return read > max ? throw TooLarge() : buffer[..read];
        }

        private static ImageException TooLarge()
            => new("image_too_large", $"La imagen supera {SaveMediaImageUseCase.MaxBytes} bytes");
    }
}
