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

    // El asset sale de AssetBuilder.PeekAsync: el abierto (sabe el progreso) o el que se reconstruye del
    // manifest con la misma regla que abrir. Un interrumpido es Pending y sale "pending" (C4). null = nada generado.
    public static EncodeStatus From(Guid idMedia, string assetId, Asset? asset)
        => asset is null
            ? Idle(idMedia, assetId)
            : new(idMedia, assetId, StateName(asset.Video.State), asset.Progress,
                ActualVideoCodec(asset.Options, asset.Strategy, asset.Info), asset.Video.Error);

    // Con el video copiado no se aplico el perfil: el codec real es el del origen.
    internal static string ActualVideoCodec(TranscodeOptions options, StreamStrategy strategy, SourceInfo info)
        => options.CanCopyVideo(strategy) ? info.VideoCodec : options.VideoCodec;

    // "Building" -> "building": el cliente y el prototipo usan minusculas.
    public static string StateName(ArtifactState state) => state.ToString().ToLowerInvariant();
}
