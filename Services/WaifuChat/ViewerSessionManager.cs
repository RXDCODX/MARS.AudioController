using System.Collections.Concurrent;

namespace MARS.AudioController.Services.WaifuChat;

public class ViewerSession
{
    public required string TwitchId { get; init; }
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}

public class ViewerSessionManager
{
    private readonly int _maxSessions;
    private readonly TimeSpan _evictionTimeout;
    private readonly ConcurrentDictionary<string, ViewerSession> _sessions = new();

    public ViewerSessionManager(int maxSessions, TimeSpan evictionTimeout)
    {
        _maxSessions = maxSessions;
        _evictionTimeout = evictionTimeout;
    }

    public int ActiveSessionCount => _sessions.Count;

    public ViewerSession GetOrCreateSession(string twitchId)
    {
        EvictStaleSessions();

        var session = _sessions.GetOrAdd(twitchId, id => new ViewerSession { TwitchId = id });
        session.LastActivity = DateTime.UtcNow;
        return session;
    }

    private void EvictStaleSessions()
    {
        if (_sessions.Count < _maxSessions)
        {
            return;
        }

        var cutoff = DateTime.UtcNow - _evictionTimeout;
        var staleKeys = _sessions
            .Where(kv => kv.Value.LastActivity < cutoff)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in staleKeys)
        {
            _sessions.TryRemove(key, out _);
        }

        // Если всё ещё превышен лимит — удаляем самые старые
        if (_sessions.Count >= _maxSessions)
        {
            var oldest = _sessions
                .OrderBy(kv => kv.Value.LastActivity)
                .Take(_sessions.Count - _maxSessions + 1)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in oldest)
            {
                _sessions.TryRemove(key, out _);
            }
        }
    }

    public void DisposeAllSessions()
    {
        _sessions.Clear();
    }
}
