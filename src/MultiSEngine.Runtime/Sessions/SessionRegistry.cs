using System.Collections.Concurrent;
using System.Collections.Immutable;
using MultiSEngine.Core.Sessions;

namespace MultiSEngine.Runtime.Sessions;

public sealed class SessionRegistry : ISessionRegistry
{
    private readonly ConcurrentDictionary<Guid, ISessionHandle> _sessions = new();

    public int Count => _sessions.Count;

    public bool TryRegister(ISessionHandle session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _sessions.TryAdd(session.SessionId, session);
    }

    public bool TryRemove(Guid sessionId, out ISessionHandle? removed)
        => _sessions.TryRemove(sessionId, out removed);

    public bool TryGet(Guid sessionId, out ISessionHandle? session)
        => _sessions.TryGetValue(sessionId, out session);

    public ISessionHandle? FindByName(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return null;

        // Exact match first, then prefix fallback.
        ISessionHandle? prefixMatch = null;
        foreach (var session in _sessions.Values)
        {
            if (string.Equals(session.PlayerName, playerName, StringComparison.OrdinalIgnoreCase))
                return session;

            if (prefixMatch is null && session.PlayerName.StartsWith(playerName, StringComparison.OrdinalIgnoreCase))
                prefixMatch = session;
        }

        return prefixMatch;
    }

    public ImmutableArray<ISessionHandle> GetSessions()
        => [.. _sessions.Values];

    public ImmutableArray<SessionSnapshot> GetSnapshots()
        => [.. _sessions.Values.Select(s => s.GetSnapshot())];
}
