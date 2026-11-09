using StreamMedia.Application.Cache.Interfaces;

namespace StreamMedia.Application.Streaming;

// POST /api/v1/heartbeat/{session_id}
// Marca la sesion y hace el touch del LRU: por eso es un use case y no una
// llamada al manager desde el controller.
public sealed class HeartbeatUseCase(SessionManager sessions, ICacheSpace space)
{
    // False si la sesion no existe (o ya expiro).
    public async Task<bool> ExecuteAsync(string sessionId, CancellationToken ct = default)
    {
        var session = sessions.Heartbeat(sessionId);
        if (session is null) return false;

        await space.TouchAsync(session.AssetId, ct); // corre el asset al frente del LRU
        return true;
    }
}