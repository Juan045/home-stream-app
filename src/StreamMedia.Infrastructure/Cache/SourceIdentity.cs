using System.Security.Cryptography;
using System.Text;
using StreamMedia.Application.Cache.Interfaces;

namespace StreamMedia.Infrastructure.Cache;

public sealed class SourceIdentity : ISourceIdentity
{
    const int AssetIdLength = 16;

    public string AssetIdFor(string source)
    {
        var file = new FileInfo(source);
        var normalized = file.FullName;
        // Windows no distingue mayusculas: la misma pelicula con otra capitalizacion es el mismo asset.
        if (OperatingSystem.IsWindows()) normalized = normalized.ToLowerInvariant();

        // ponytail: no resuelve symlinks (Python .resolve() si); dos rutas al mismo archivo
        // generan dos assets. Inofensivo salvo por el disco.
        var raw = $"{normalized}|{file.LastWriteTimeUtc.Ticks}|{file.Length}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(hash)[..AssetIdLength];
    }
}
