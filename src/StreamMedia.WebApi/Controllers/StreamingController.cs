using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Application.Streaming;

namespace StreamMedia.WebApi.Controllers
{
    [Route("api/v1/[controller]")]
    [ApiController]
    public class StreamingController(StartStreamUseCase startStreamUseCase,
        HeartbeatUseCase heartbeatUseCase) : ControllerBase
    {
        public record StreamRequest(Guid idMedia, string? filePath);

        // TODO: aceptar unicamente el idMedia, eliminar el filePath ya que se eliminara el CLI.
        [HttpPost("stream")]
        public async Task<IActionResult> Post([FromBody] StreamRequest request, CancellationToken ct)
        {
            try
            {
                if (request.filePath is not null)
                {
                    await startStreamUseCase.ExecuteAsync(request.filePath, ct);
                }
            }
            catch (MediaPathException ex)
            {
                return BadRequest(new { error = ex.Code, detail = ex.Message });
            }

            return StatusCode(StatusCodes.Status501NotImplemented,
                new { error = "not_implemented", detail = "POST /stream: pendiente de implementacion" });
        }


        [HttpPost("heartbeat/{sessionId}")]
        public IActionResult Heartbeat(string sessionId)
        {
            if (!heartbeatUseCase.Execute(sessionId))
                {
                    return NotFound(new {error="session_not_found", detail=$"La session {sessionId} no existe"});
                }

            return NoContent();
        }
    }
}