using StreamMedia.Application.Encoder;
using StreamMedia.Application.MediaExceptions;
using StreamMedia.Application.Streaming.Interfaces;
using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Streaming;

/// <summary>
/// Arma las playlists que ve el cliente. Se calculan en cada request: el internal.m3u8 de FFmpeg
/// solo se parsea para sacar las duraciones reales y nunca se sirve.
/// </summary>
public sealed class GetPlaylistUseCase(AssetBuilder builder, IAssetStore store)
{
    // ponytail: respaldo para cuando ffprobe no informa bit_rate.
    const long FallbackBandwidth = 4_000_000;

    /// <summary>Master con el video y una rendition de audio por pista registrada. No toca disco.</summary>
    public string Master(string assetId)
    {
        var asset = GetAsset(assetId);

        (int Width, int Height)? resolution =
            asset.Info is { Width: > 0, Height: > 0 } i ? (i.Width, i.Height) : null;

        return PlaylistBuilder.BuildMasterPlaylist(
            videoUri: "video/playlist.m3u8",
            renditions: Renditions(asset),
            bandwidth: asset.Info.BitRate is { } bitRate and > 0 ? bitRate : FallbackBandwidth,
            videoCodec: VideoCodec(asset),
            resolution: resolution);
    }

    public async Task<string> VideoAsync(string assetId, CancellationToken ct = default)
    {
        var asset = GetAsset(assetId);
        return await MediaAsync(asset, store.Paths(asset.Id).Video, ct);
    }

    public async Task<string> AudioAsync(string assetId, int track, CancellationToken ct = default)
    {
        var asset = GetAsset(assetId);

        // Una pista que no existe es un 404 de verdad, no un "todavia no".
        if (!asset.Audio.ContainsKey(track))
            throw new StreamNotFoundException("track_not_found", $"El asset {assetId} no tiene la pista {track}");

        return await MediaAsync(asset, store.Paths(asset.Id).Audio(track), ct);
    }

    Asset GetAsset(string assetId)
        => builder.Get(assetId)
           ?? throw new StreamNotFoundException("asset_not_found", $"El asset {assetId} no existe");

    // Mientras el internal.m3u8 no tenga ENDLIST sale como EVENT; terminado, como VOD.
    async Task<string> MediaAsync(Asset asset, string directory, CancellationToken ct)
    {
        var text = await store.ReadInternalPlaylistAsync(directory, ct)
            ?? throw new PlaylistNotReadyException(
                $"El asset {asset.Id} todavia se esta generando. Reintentar en unos segundos.");

        return PlaylistBuilder.BuildMediaPlaylist(
            PlaylistBuilder.ParseInternalPlaylist(text),
            complete: PlaylistBuilder.IsComplete(text));
    }

    // CODECS: los dos o ninguno (lo garantiza BuildMasterPlaylist). Con el video copiado sale del
    // perfil/nivel de ffprobe, y si no alcanzan AvcCodecString da null y se omite todo el atributo.
    // Con codificacion sale del registro.
    static string? VideoCodec(Asset asset)
        => asset.Options.CanCopyVideo(asset.Strategy)
            ? PlaylistBuilder.AvcCodecString(asset.Info.VideoProfile, asset.Info.VideoLevel)
            : VideoEncoders.Get(asset.Options.VideoCodec).CodecString;

    // Solo las pistas registradas. Los nombres se calculan sobre TODAS las pistas del archivo,
    // como el Python: asi un "(2)" no cambia de pista cuando cambia la seleccion.
    static List<AudioRendition> Renditions(Asset asset)
    {
        var names = UniqueNames(asset.Info.AudioTracks);

        return [.. asset.Info.AudioTracks
            .Where(t => asset.Audio.ContainsKey(t.Index))
            .Select(t => new AudioRendition(
                Uri: $"audio/{t.Index}/playlist.m3u8",
                Name: names[t.Index],
                Language: t.Language,
                Channels: asset.Options.CanCopyAudio(t) ? t.Channels : asset.Options.AudioChannels,
                Codec: asset.Options.CanCopyAudio(t) ? PlaylistBuilder.AacCodecString(t.Profile) : PlaylistBuilder.AacLc,
                Default: t.Index == 0))];
    }

    // Dos pistas con el mismo idioma y sin titulo darian el mismo NAME y el selector quedaria ambiguo.
    static Dictionary<int, string> UniqueNames(IReadOnlyList<AudioTrack> tracks)
    {
        var names = new Dictionary<int, string>();
        var seen = new Dictionary<string, int>();

        foreach (var track in tracks)
        {
            var title = track.Title.Trim();
            var name = title.Length > 0 ? title : track.Language;
            var count = seen[name] = seen.GetValueOrDefault(name) + 1;
            names[track.Index] = count == 1 ? name : $"{name} ({count})";
        }
        return names;
    }
}