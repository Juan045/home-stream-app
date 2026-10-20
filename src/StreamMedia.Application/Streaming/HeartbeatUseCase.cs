using StreamMedia.Application.Streaming.Interfaces;

namespace StreamMedia.Application.Streaming;

// POST /api/v1/heartbeat/{session_id}
// Hoy solo marca la sesion. Cuando exista AssetStore, aca entra el touch
// del LRU: por eso vale tener el use case y no llamar al manager desde el controller.
public sealed class HeartbeatUseCase(SessionManager sessions, IAssetStore store)
{
    // False si la sesion no existe (o ya expiro).
    public async Task<bool> ExecuteAsync(string sessionId, CancellationToken ct = default)
    {
        var session = sessions.Heartbeat(sessionId);
        if (session is null) return false;

        await store.TouchAsync(session.AssetId, ct); // corre el asset al frente del LRU
        return true;
    }
}