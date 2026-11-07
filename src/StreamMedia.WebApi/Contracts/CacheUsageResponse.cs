using StreamMedia.Application.Cache;

namespace StreamMedia.WebApi.Contracts;

/// <summary>Uso del cache. Sale en snake_case por la politica global de JSON (Program.cs).</summary>
public sealed record CacheUsageResponse(long UsedBytes, long MaxBytes)
{
    public static CacheUsageResponse From(CacheUsage usage) => new(usage.UsedBytes, usage.MaxBytes);
}
