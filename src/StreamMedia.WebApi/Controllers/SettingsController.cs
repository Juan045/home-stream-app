using Microsoft.AspNetCore.Mvc;
using StreamMedia.Application.Encoder;
using StreamMedia.WebApi.Contracts;

namespace StreamMedia.WebApi.Controllers
{
    [ApiController]
    [Route("api/v1/settings")]
    public class SettingsController(
        GetEncodingSettingsUseCase getEncodingSettingsUseCase,
        UpdateEncodingSettingsUseCase updateEncodingSettingsUseCase) : ControllerBase
    {
        [HttpGet("encoding")]
        public async Task<IActionResult> GetEncoding(CancellationToken ct)
        {
            var settings = await getEncodingSettingsUseCase.ExecuteAsync(ct);
            return Ok(EncodingSettingsResponse.From(settings));
        }

        [HttpPut("encoding")]
        public async Task<IActionResult> PutEncoding(UpdateEncodingSettingsRequest request, CancellationToken ct)
        {
            var settings = await updateEncodingSettingsUseCase.ExecuteAsync(
                request.LiveCodec, request.ArchiveCodec, ct);
            return Ok(EncodingSettingsResponse.From(settings));
        }
    }
}
