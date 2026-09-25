using StreamMedia.Application.Media.Interfaces;
using StreamMedia.Domain.Entities;

namespace StreamMedia.Infrastructure.Media;

// Implementacion temporal de IMediaAnalyzer.
// No invoca ffprobe todavia: devuelve datos inventados para poder
// probar RegisterMediaUseCase sin depender de un binario externo.
public sealed class FakeMediaAnalyzer : IMediaAnalyzer
{
    public Task<SourceInfo> AnalyzeAsync(string relativePath, CancellationToken ct)
    {
        // Devuelve datos inventados para poder testear el flujo de punta a punta
        var info = new SourceInfo(
            Duration: 3600,
            VideoCodec: "h264",
            Width: 1920,
            Height: 1080,
            AudioTracks: [new AudioTrack(Index: 0, Language: "spa", Codec: "aac")],
            SubtitleTracks: [new SubtitleTrack(Index: 0, Language: "spa")]
        );

        return Task.FromResult(info);
    }
}