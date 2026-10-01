namespace StreamMedia.Application.Streaming;

// POST /api/v1/heartbeat/{session_id}
// Hoy solo marca la sesion. Cuando exista AssetStore, aca entra el touch
// del LRU: por eso vale tener el use case y no llamar al manager desde el controller.
public sealed class HeartbeatUseCase(SessionManager sessions)
{
    public bool Execute(string sessionId) => sessions.Heartbeat(sessionId);
}