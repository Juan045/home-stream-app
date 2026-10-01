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

    public Session? Get(string sessionId)
        => _sessions.TryGetValue(sessionId, out var session) ? session : null;

    // False si no existe (o ya expiro).
    public bool Heartbeat(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return false;

        session.Touch(time.GetTimestamp());
        return true;
    }

    // Assets con alguna sesion viva. El GC no puede tocarlos.
    public HashSet<string> ReferencedAssetIds()
        => _sessions.Values.Select(s => s.AssetId).ToHashSet();

    // Elimina las sesiones expiradas y devuelve sus ids.
    public List<string> Prune()
    {
        var now = time.GetTimestamp();
        var expired = new List<string>();

        foreach (var (id, session) in _sessions)
        {
            if (time.GetElapsedTime(session.LastSeen, now) >= ExpireAfter
                && _sessions.TryRemove(id, out _))
                expired.Add(id);
        }

        return expired;
    }
}