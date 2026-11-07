namespace StreamMedia.Application.Cache;

/// <summary>Lo que ocupa el cache en disco y el tope configurado (Cache:MaxBytes), en bytes.</summary>
public sealed record CacheUsage(long UsedBytes, long MaxBytes);
