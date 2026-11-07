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
        // multipart/form-data con el archivo en el campo "file": la forma estandar de subir archivos
        // chicos en ASP.NET (IFormFile, buffered). Por el IFormFile, [ApiController] infiere
        // [Consumes("multipart/form-data")]; un body que no es multipart deberia dar 415, pero hoy
        // lo atiende el catch-all de /api con un 404 (TODO #23).
        [HttpPut]
        public async Task<IActionResult> Put(Guid id, MediaImageRole role, IFormFile? file, CancellationToken ct)
        {
            var data = await ReadFileAsync(file, ct);
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

        // El parametro es nullable a proposito: sin el campo "file" responde el 422 propio,
        // con { error, detail }, y no el 400 automatico de [ApiController].
        // El tamano se mira antes de copiar a memoria.
        // ponytail: ASP.NET ya bufferea el archivo antes de llegar aca (a un temporal en disco
        // si pasa 64 KB), hasta el tope de Kestrel (30 MB). Si molesta, bajar ese tope con
        // [RequestFormLimits(MultipartBodyLengthLimit = ...)].
        private static async Task<byte[]> ReadFileAsync(IFormFile? file, CancellationToken ct)
        {
            if (file is null)
                throw new ImageException("invalid_image", "Falta el archivo en el campo file");

            if (file.Length > SaveMediaImageUseCase.MaxBytes)
                throw new ImageException("image_too_large", $"La imagen supera {SaveMediaImageUseCase.MaxBytes} bytes");

            var data = new byte[file.Length];
            await using var stream = file.OpenReadStream();
            await stream.ReadExactlyAsync(data, ct);
            return data;
        }
    }
}
