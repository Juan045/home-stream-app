using System.Collections.Concurrent;
using StreamMedia.Domain.Entities;

namespace StreamMedia.Application.Streaming;

// Estado en memoria de las sesiones. Es singleton: se pierde al reiniciar.
public sealed class SessionManager(TimeProvider time)
{
    // Se conserva un rato despues del ultimo latido: recargar la pagina
    // no deberia costar reconstruir nada.
    private static readonly TimeSpan ExpireAfter = TimeSpan.FromMinutes(12); // 12 minutos sin actividad = expira.

    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    public Session Create(string assetId)
    {
        var session = new Session(Guid.NewGuid().ToString("N")[..12], assetId, time.GetTimestamp());
        _sessions[session.Id] = session;
        return session;
    }

    // Null si no existe o ya expiro, aunque Prune todavia no la haya sacado.
    public Session? Get(string sessionId)
        => _sessions.TryGetValue(sessionId, out var session) && !Expired(session, time.GetTimestamp())
            ? session
            : null;

    // Null si no existe o ya expiro: un latido tardio no revive la sesion.
    public Session? Heartbeat(string sessionId)
    {
        if (Get(sessionId) is not { } session)
            return null;

        session.Touch(time.GetTimestamp());
        return session;
    }

    /// <summary>Assets con alguna sesion viva, activa o no. El GC no puede tocarlos.</summary>
    public HashSet<string> ReferencedAssetIds()
        => _sessions.Values.Select(s => s.AssetId).ToHashSet();

    // Elimina las sesiones expiradas y devuelve sus ids.
    public List<string> Prune()
    {
        var now = time.GetTimestamp();
        var expired = new List<string>();

        foreach (var (id, session) in _sessions)
        {
            if (Expired(session, now) && _sessions.TryRemove(id, out _))
                expired.Add(id);
        }

        return expired;
    }

    private bool Expired(Session session, long now)
        => time.GetElapsedTime(session.LastSeen, now) >= ExpireAfter;
}