using StreamMedia.Application.Streaming;
using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Encoder;

// Estado de la codificacion de biblioteca, en el vocabulario que ve el cliente
// (idle/pending/building/ready/failed). Es la misma informacion que StreamStarted le da al
// player, recortada a lo que dibuja la ficha: no hay sesion ni reproduccion.
public sealed record EncodeStatus(
    Guid IdMedia, string? AssetId, string State, double Progress, string? VideoCodec, string? Error)
{
    public static EncodeStatus Idle(Guid idMedia, string? assetId)
        => new(idMedia, assetId, "idle", 0, null, null);

    // Memoria primero: es la unica que sabe el progreso. El manifest es el que contesta
    // despues de un reinicio, porque los artefactos siguen en disco aunque nadie los abra.
    public static EncodeStatus From(Guid idMedia, string assetId, Asset? asset, AssetManifest? manifest)
    {
        if (asset is not null)
            return new(idMedia, assetId, StateName(asset.Video.State), asset.Progress,
                asset.Options.VideoCodec, asset.Video.Error);

        if (manifest is not null)
            return new(idMedia, assetId, StateName(manifest.Video),
                manifest.Video == ArtifactState.Ready ? 1 : 0,
                manifest.Options?.VideoCodec, null);

        return Idle(idMedia, assetId);
    }

    // "Building" -> "building": el cliente y el prototipo usan minusculas.
    public static string StateName(ArtifactState state) => state.ToString().ToLowerInvariant();
}
