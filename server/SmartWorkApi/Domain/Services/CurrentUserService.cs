using Microsoft.EntityFrameworkCore;

public interface ICurrentUserService
{
    Task<User?> GetCurrentUserAsync(HttpRequest request, AppDbContext db, CancellationToken cancellationToken = default);
}

public class CurrentUserService : ICurrentUserService
{
    private readonly ISessionStore _sessionStore;

    public CurrentUserService(ISessionStore sessionStore)
    {
        _sessionStore = sessionStore;
    }

    // Resolve authenticated user by bearer token and fetch fresh user data from DB.
    public Task<User?> GetCurrentUserAsync(HttpRequest request, AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (!request.Headers.TryGetValue("Authorization", out var authHeader)) return Task.FromResult<User?>(null);

        var value = authHeader.ToString();
        if (!value.StartsWith("Bearer ")) return Task.FromResult<User?>(null);

        var token = value.Substring("Bearer ".Length).Trim();
        if (!_sessionStore.TryGetUsername(token, out var username)) return Task.FromResult<User?>(null);

        return db.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
    }
}