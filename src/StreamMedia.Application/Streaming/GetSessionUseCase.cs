using StreamMedia.Application.MediaExceptions;
using StreamMedia.Application.Streaming.Interfaces;

namespace StreamMedia.Application.Streaming;

// GET /api/v1/sessions/{id}: estado del build. El player lo consulta cada 2 s.
// No renueva la sesion (solo el heartbeat lo hace). Una vencida da session_not_found.
public sealed class GetSessionUseCase(SessionManager sessions, AssetBuilder builder, IAssetStore store)
{
    public StreamStarted Execute(string sessionId)
    {
        var session = sessions.Get(sessionId)
            ?? throw new StreamNotFoundException("session_not_found", $"La sesion {sessionId} no existe");

        var asset = builder.Get(session.AssetId)
            ?? throw new StreamNotFoundException("asset_not_found", "El asset de la sesion ya no esta disponible");

        return new StreamStarted(asset, session, AssetPlayability.IsPlayable(asset, store));
    }
}