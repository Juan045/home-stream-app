namespace StreamMedia.Infrastructure.Cache;

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>Directorio del cache. Si es relativo, cuenta desde el directorio de trabajo del proceso.</summary>
    public string Dir { get; init; } = "output";

    /// <summary>Tope del cache en bytes (50 GiB por defecto). Al superarlo entra el GC por LRU.</summary>
    public long MaxBytes { get; init; } = 50L * 1024 * 1024 * 1024;
}