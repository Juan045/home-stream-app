using Microsoft.AspNetCore.Mvc;
using StreamMedia.Application.Streaming;

namespace StreamMedia.WebApi.Controllers;

// Las playlists HLS. Cuelgan de /hls y no de /api/v1: son lo que consume el player, no la API.
[ApiController]
[Route("hls/{assetId}")]
public class HlsController(
    GetPlaylistUseCase playlists,
    GetHlsFileUseCase files) : ControllerBase
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

    // TODO: whitelist por nombre. Hoy el use case filtra por extensión (.mp4, .m4s, .vtt), lo
    // mínimo para que el internal.m3u8 no salga. Lo estricto sería una route constraint, por
    // ejemplo {file:regex(^(init\.mp4|seg-\d{{5}}\.m4s)$)}, de modo que lo que no es del layout
    // ni matchee la ruta. Queda pendiente porque obliga a revisar los nombres que pide el frontend.
    [HttpGet("video/{file}")]
    public IActionResult VideoFile(string assetId, string file)
        => Serve(files.Video(assetId, file));

    [HttpGet("audio/{track:int}/{file}")]
    public IActionResult AudioFile(string assetId, int track, string file)
        => Serve(files.Audio(assetId, track, file));

    [HttpGet("subs/{file}")]
    public IActionResult SubtitleFile(string assetId, string file)
        => Serve(files.Subtitle(assetId, file));

    // enableRangeProcessing: responde 206 a los pedidos con cabecera Range.
    IActionResult Serve((string FullPath, string ContentType) file)
        => PhysicalFile(file.FullPath, file.ContentType, enableRangeProcessing: true);
}