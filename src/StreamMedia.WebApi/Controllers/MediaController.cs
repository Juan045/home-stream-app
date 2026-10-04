using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StreamMedia.Application.Media;
using StreamMedia.Domain.DTO;
using StreamMedia.Application.MediaExceptions;

namespace StreamMedia.WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public class MediaController(
        RegisterMediaUseCase registerMediaUseCase,
        GetMediaByIdUseCase getMediaByIdUseCase,
        ListMediaUseCase listMediaUseCase,
        UpdateMediaUseCase updateMediaUseCase) : ControllerBase
    {
        public record CreateMediaRequest(string FilePath);

        // DTOs para no retornar la entidad completa (que incluye info sensible) en la lista de resultados
        public record MediaListItemResponse(Guid IdMedia, string Title, string Kind, int? Year, double Duration);
        public record MediaListResponse(IReadOnlyList<MediaListItemResponse> Items, int Total, int Limit, int Offset);

        public record UpdateMediaRequest(string? title, string? kind, string? synopsis, List<int>? ignored_audio, List<int>? ignored_subtitles);


        // dos veces la misma pelicula da error no esta controlado.
        [HttpPost]
        public async Task<IActionResult> Post(CreateMediaRequest request, CancellationToken ct)
        {
            try
            {
                var result = await registerMediaUseCase.ExecuteAsync(request.FilePath, ct);
                if (result.IsAlreadyExists)
                    return Conflict(new { error = "media_already_exists", detail = result.ExistingId });

                return Created($"/api/v1/media/{result.Media!.Id}", result.Media);
            } catch (MediaPathException ex)
            {
                var body = new { error = ex.Code, detail = ex.Message };
                return ex.Code == "file_not_found" ? NotFound(body) : BadRequest(body);
            }
            catch (InvalidMediaException ex)
            {
                return BadRequest(new { error = "invalid_media", detail = ex.Message });
            }
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        {
            var media = await getMediaByIdUseCase.ExecuteAsync(id, ct);

            if (media is null)
                return NotFound(new { error = "media_not_found", detail = $"No existe una ficha con id {id}" });

            return Ok(media);
        }
    
        [HttpGet]
        public async Task<IActionResult> GetList(
            [FromQuery] string? q,
            [FromQuery] string? kind,
            [FromQuery(Name = "in_list")] bool? inList,
            [FromQuery] string sort = "title",
            [FromQuery] int limit = 10,
            [FromQuery] int offset = 0,
            CancellationToken ct = default)
        {
            limit = Math.Clamp(limit, 1, 100);
            offset = Math.Max(offset, 0);

            var page = await listMediaUseCase.ExecuteAsync(
                new MediaListQuery(q, kind, inList, sort, limit, offset), ct);

            var items = page.Items
                .Select(m => new MediaListItemResponse(m.Id, m.Title, m.Kind, m.Year, m.SourceInfo!.Duration))
                .ToList();

            var response = new MediaListResponse(items, page.Total, limit, offset);

            return Ok(response);
        }


        [HttpPatch("{id:guid}")]
        public async Task<IActionResult> Patch(Guid id, [FromBody] UpdateMediaRequest request, CancellationToken ct)
        {
            var mediaDTO = new UpdateMediaDTO
            {
                title = request.title,
                kind = request.kind,
                ignored_audio = request.ignored_audio?.ToArray(),
                ignored_subtitles = request.ignored_subtitles?.ToArray()
            };

            var result = await updateMediaUseCase.ExecuteAsync(id, mediaDTO, ct);

            if (result.IsNotFound)
                return NotFound(new { error = "media_not_found", detail = $"No existe una ficha con id {id}" });

            return Ok(result.Media);
        }
    }
}
