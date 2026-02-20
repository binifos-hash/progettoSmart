public interface ISessionStore
{
    string CreateSession(string username);
    bool TryGetUsername(string token, out string username);
}