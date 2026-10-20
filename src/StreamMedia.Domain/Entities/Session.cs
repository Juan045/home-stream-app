namespace StreamMedia.Domain.Entities;

// Un espectador mirando un asset. No es duena de ningun archivo.
public sealed class Session(string id, string assetId, long lastSeen)
{
    public string Id { get; } = id;
    public string AssetId { get; } = assetId;

    // Timestamp de TimeProvider (no es una fecha): solo sirve para medir
    // cuanto paso desde el ultimo latido.
    public long LastSeen { get; private set; } = lastSeen;

    public void Touch(long timestamp) => LastSeen = timestamp;
}