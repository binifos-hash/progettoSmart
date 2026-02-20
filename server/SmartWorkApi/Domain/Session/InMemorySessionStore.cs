using System.Collections.Concurrent;

public class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, string> _sessions = new();

    public string CreateSession(string username)
    {
        var token = Guid.NewGuid().ToString();
        _sessions[token] = username;
        return token;
    }

    public bool TryGetUsername(string token, out string username)
    {
        return _sessions.TryGetValue(token, out username!);
    }
}