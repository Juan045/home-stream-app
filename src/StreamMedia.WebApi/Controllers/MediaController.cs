using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StreamMedia.Application.Media;

namespace StreamMedia.WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class MediaController(RegisterMediaUseCase registerMediaUseCase) : ControllerBase
    {
        public record CreateMediaRequest(string FilePath);

        [HttpPost]
        public async Task<IActionResult> Post(CreateMediaRequest request, CancellationToken ct)
        {
            var result = await registerMediaUseCase.ExecuteAsync(request.FilePath, ct);

            if (result.IsAlreadyExists)
            return Conflict(new { error = "media_already_exists", detail = result.ExistingId });

            return Created($"/api/v1/media/{result.Media!.Id}", result.Media);
        }
    }
}
