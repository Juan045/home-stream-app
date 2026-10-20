using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StreamMedia.WebApi.Contracts;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Application.Streaming;

namespace StreamMedia.WebApi.Controllers
{
    [Route("api/v1/")]
    [ApiController]
    public class StreamingController(
        StartStreamUseCase startStreamUseCase,
        GetSessionUseCase getSessionUseCase,
        HeartbeatUseCase heartbeatUseCase) : ControllerBase
    {
        public record StreamRequest(string? idMedia, string? filePath);

        // TODO: aceptar unicamente el idMedia, eliminar el filePath ya que se eliminara el CLI.
        /*
            enviar idMedia vacio retorna 404 Not Found sin body (Cambiar)
        */
        [HttpPost("stream")]
        public async Task<IActionResult> Post([FromBody] StreamRequest request, CancellationToken ct)
        {
            var started = await startStreamUseCase.ExecuteAsync(request.filePath, request.idMedia, ct);
            return StatusCode(StatusCodes.Status201Created,
                StreamResponse.From(started.Asset, started.Session.Id, started.Playable));
        }

        [HttpGet("sessions/{sessionId}")]
        public IActionResult GetSession(string sessionId)
        {
            var state = getSessionUseCase.Execute(sessionId);
            return Ok(StreamResponse.From(state.Asset, state.Session.Id, state.Playable));
        }

        [HttpPost("heartbeat/{sessionId}")]
        public async Task<IActionResult> Heartbeat(string sessionId, CancellationToken ct)
        {
            if (!await heartbeatUseCase.ExecuteAsync(sessionId, ct))
                return NotFound(new { error = "session_not_found", detail = $"La sesion {sessionId} no existe" });

            return NoContent();
        }
    }
}