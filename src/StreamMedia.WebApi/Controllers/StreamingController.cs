using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StreamMedia.WebApi.Contracts;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Application.Streaming;

namespace StreamMedia.WebApi.Controllers
{
    [Route("api/v1/[controller]")]
    [ApiController]
    public class StreamingController(
        StartStreamUseCase startStreamUseCase,
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
            try
            {
                var started = await startStreamUseCase.ExecuteAsync(request.filePath, request.idMedia, ct);
                return StatusCode(StatusCodes.Status201Created,
                    StreamResponse.From(started.Asset, started.Session.Id, started.Playable));
            }
            catch (MediaPathException ex)
            {
                var status = ex.Code is "file_not_found" or "media_not_found"
                    ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
                return StatusCode(status, new { error = ex.Code, detail = ex.Message });
            }
            catch (Exception ex) when (ex is InvalidMediaException or FfmpegException)
            {
                return BadRequest(new { error = "invalid_media", detail = ex.Message });
            }
            catch (CapacityException ex)
            {
                var status = ex.Code == "too_many_jobs"
                    ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status507InsufficientStorage;
                return StatusCode(status, new { error = ex.Code, detail = ex.Message });
            }
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