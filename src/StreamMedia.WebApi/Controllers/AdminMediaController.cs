using Microsoft.AspNetCore.Mvc;
using StreamMedia.Application.Media;
using StreamMedia.WebApi.Contracts;

namespace StreamMedia.WebApi.Controllers
{
    // Lecturas del panel de admin. El prefijo ordena las rutas y deja un solo lugar que proteger
    // cuando haya autenticacion: hoy no protege nada (TODO #18).
    [ApiController]
    [Route("api/v1/admin/media")]
    public class AdminMediaController(ListMediaUseCase listMediaUseCase) : ControllerBase
    {
        // Mismo listado que /media con la ficha completa por fila. El repositorio ya trae el
        // SourceInfo entero en cada fila: no cuesta otra consulta ni un stat al disco.
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

            var items = page.Items.Select(MediaResponse.From).ToList();

            return Ok(new AdminMediaListResponse(items, page.Total, limit, offset));
        }
    }
}
