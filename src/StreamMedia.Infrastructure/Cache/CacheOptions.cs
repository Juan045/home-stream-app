namespace StreamMedia.Infrastructure.Cache;

public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>Directorio del cache. Si es relativo, cuenta desde el directorio de trabajo del proceso.</summary>
    public string Dir { get; init; } = "output";
}