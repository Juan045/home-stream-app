using Microsoft.AspNetCore.Mvc;
using StreamMedia.Application.Cache;
using StreamMedia.WebApi.Contracts;

namespace StreamMedia.WebApi.Controllers
{
    // Lecturas del panel de admin sobre el cache. Mismo prefijo que AdminMediaController (TODO #18).
    [ApiController]
    [Route("api/v1/admin/cache")]
    public class AdminCacheController(GetCacheUsageUseCase getCacheUsageUseCase) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> GetUsage(CancellationToken ct)
        {
            var usage = await getCacheUsageUseCase.ExecuteAsync(ct);
            return Ok(CacheUsageResponse.From(usage));
        }
    }
}
