using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Media.Interfaces;

// El equivalente a "media_analyzer.py": correr ffprobe y devolver SourceInfo.
// Infrastructure lo va a implementar invocando el proceso ffprobe.
public interface IMediaAnalyzer
{
    Task<SourceInfo> AnalyzeAsync(string absolutePath, CancellationToken ct = default);
}
