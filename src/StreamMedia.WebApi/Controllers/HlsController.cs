using Microsoft.AspNetCore.Mvc;
using StreamMedia.Application.Streaming;

namespace StreamMedia.WebApi.Controllers;

// Las playlists HLS. Cuelgan de /hls y no de /api/v1: son lo que consume el player, no la API.
[ApiController]
[Route("hls/{assetId}")]
public class HlsController(GetPlaylistUseCase playlists) : ControllerBase
{
    const string PlaylistContentType = "application/vnd.apple.mpegurl";

    [HttpGet("master.m3u8")]
    public IActionResult Master(string assetId)
        => Content(playlists.Master(assetId), PlaylistContentType);

    [HttpGet("video/playlist.m3u8")]
    public async Task<IActionResult> Video(string assetId, CancellationToken ct)
        => Content(await playlists.VideoAsync(assetId, ct), PlaylistContentType);

    [HttpGet("audio/{track:int}/playlist.m3u8")]
    public async Task<IActionResult> Audio(string assetId, int track, CancellationToken ct)
        => Content(await playlists.AudioAsync(assetId, track, ct), PlaylistContentType);
}