using Microsoft.AspNetCore.Mvc;
using StreamMedia.Application.Encoder;
using StreamMedia.WebApi.Contracts;

namespace StreamMedia.WebApi.Controllers
{
    // Codificacion asincrona de biblioteca. La ruta queda bajo /media/{id}/encode porque es
    // el contrato que ya consume el frontend; separarla en su controller deja la logica de
    // encoder en un solo lugar. El controller solo orquesta los casos de uso.
    [ApiController]
    [Route("api/v1/media")]
    public class EncoderController(
        EncodeMediaUseCase encodeMediaUseCase,
        GetEncodeStatusUseCase getEncodeStatusUseCase) : ControllerBase
    {
        // 202 y vuelve enseguida: la codificacion puede tardar horas. El Location apunta al
        // GET que informa el avance.
        [HttpPost("{id:guid}/encode")]
        public async Task<IActionResult> Post(Guid id, CancellationToken ct)
        {
            var status = await encodeMediaUseCase.ExecuteAsync(id, ct);
            return Accepted($"/api/v1/media/{id}/encode", EncodeResponse.From(status));
        }

        [HttpGet("{id:guid}/encode")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var status = await getEncodeStatusUseCase.ExecuteAsync(id, ct);
            return Ok(EncodeResponse.From(status));
        }
    }
}
