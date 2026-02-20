using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors();

var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (string.IsNullOrWhiteSpace(databaseUrl))
{
    throw new InvalidOperationException("DATABASE_URL environment variable is required.");
}

var connectionString = NormalizeDatabaseUrl(databaseUrl);
LogDbConnectionAttempt(connectionString, "startup-check");
TryOpenDbConnection(connectionString);

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

var app = builder.Build();

var allowedOrigins = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS")
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(o => o.Trim().TrimEnd('/'))
    .Where(o => !string.IsNullOrWhiteSpace(o))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray() ?? Array.Empty<string>();

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Urls.Add($"http://0.0.0.0:{port}");

app.UseCors(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials());

var sessions = new ConcurrentDictionary<string, string>();

app.MapGet("/", () => Results.Ok(new { message = "SmartWork API" }));

app.MapPost("/auth/login", async (LoginDto dto, AppDbContext db) =>
{
    if (dto == null) return Results.BadRequest();

    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == dto.Username);
    if (user == null) return Results.Unauthorized();

    var verified = false;
    if (!string.IsNullOrWhiteSpace(user.PasswordHash))
    {
        verified = PasswordHelper.VerifyPassword(user.PasswordHash, dto.Password);
    }
    else if (!string.IsNullOrEmpty(user.Password))
    {
        verified = user.Password == dto.Password;
        if (verified)
        {
            user.PasswordHash = PasswordHelper.HashPassword(dto.Password);
            user.Password = null;
            user.PasswordSetAt = DateTime.UtcNow;
            user.ForcePasswordChange = true;
            await db.SaveChangesAsync();
        }
    }

    if (!verified) return Results.Unauthorized();

    if (user.PasswordSetAt == null || user.PasswordSetAt <= DateTime.UtcNow.AddMonths(-4))
    {
        user.ForcePasswordChange = true;
        await db.SaveChangesAsync();
    }

    var token = Guid.NewGuid().ToString();
    sessions[token] = user.Username;

    return Results.Ok(new
    {
        token,
        username = user.Username,
        role = user.Role,
        email = user.Email,
        theme = user.Theme,
        forcePasswordChange = user.ForcePasswordChange
    });
});

app.MapPost("/auth/forgot-password", async (ForgotDto dto, AppDbContext db) =>
{
    if (dto == null || string.IsNullOrWhiteSpace(dto.Email)) return Results.BadRequest();

    var user = await db.Users.FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == dto.Email.ToLower());
    if (user == null) return Results.NotFound();

    var tmp = PasswordHelper.GenerateTemporaryPassword(10);
    user.PasswordHash = PasswordHelper.HashPassword(tmp);
    user.PasswordSetAt = DateTime.UtcNow;
    user.ForcePasswordChange = true;
    await db.SaveChangesAsync();

    var emailService = new EmailService();
    var sent = await emailService.SendTemporaryPasswordAsync(user.Email ?? "", user.Username, tmp);
    if (!sent)
    {
        return Results.Problem("Failed to send temporary password email. Check SMTP settings/logs.", statusCode: 500);
    }

    return Results.Ok(new { message = "Temporary password sent" });
});

app.MapPost("/me/change-password", async (HttpRequest req, ChangePasswordDto dto, AppDbContext db) =>
{
    var user = await GetUserFromRequest(req, db);
    if (user == null) return Results.Unauthorized();
    if (dto == null || string.IsNullOrWhiteSpace(dto.NewPassword)) return Results.BadRequest();

    if (!user.ForcePasswordChange)
    {
        if (string.IsNullOrWhiteSpace(dto.OldPassword)) return Results.BadRequest();

        var ok = false;
        if (!string.IsNullOrWhiteSpace(user.PasswordHash)) ok = PasswordHelper.VerifyPassword(user.PasswordHash, dto.OldPassword);
        else if (!string.IsNullOrEmpty(user.Password)) ok = user.Password == dto.OldPassword;
        if (!ok) return Results.Forbid();
    }

    user.PasswordHash = PasswordHelper.HashPassword(dto.NewPassword);
    user.PasswordSetAt = DateTime.UtcNow;
    user.ForcePasswordChange = false;
    user.Password = null;
    await db.SaveChangesAsync();

    return Results.Ok(new { message = "Password changed" });
});

app.MapGet("/me", async (HttpRequest req, AppDbContext db) =>
{
    var user = await GetUserFromRequest(req, db);
    if (user == null) return Results.Unauthorized();
    return Results.Ok(new { username = user.Username, role = user.Role, email = user.Email, theme = user.Theme });
});

app.MapPost("/me/theme", async (HttpRequest req, ThemeDto dto, AppDbContext db) =>
{
    var user = await GetUserFromRequest(req, db);
    if (user == null) return Results.Unauthorized();
    if (dto == null || string.IsNullOrWhiteSpace(dto.Theme)) return Results.BadRequest();

    user.Theme = dto.Theme;
    await db.SaveChangesAsync();
    return Results.Ok(new { theme = user.Theme });
});

app.MapGet("/requests", async (HttpRequest req, AppDbContext db) =>
{
    var user = await GetUserFromRequest(req, db);
    if (user == null || user.Role != "Admin") return Results.Unauthorized();

    var list = await db.Requests.OrderBy(r => r.Date).ToListAsync();
    return Results.Ok(list);
});

app.MapGet("/users", async (HttpRequest req, AppDbContext db) =>
{
    var user = await GetUserFromRequest(req, db);
    if (user == null || user.Role != "Admin") return Results.Unauthorized();

    var list = await db.Users
        .OrderBy(u => u.Username)
        .Select(u => new { username = u.Username, displayName = u.DisplayName, email = u.Email, role = u.Role })
        .ToListAsync();

    return Results.Ok(list);
});

app.MapPost("/users", async (HttpRequest req, CreateUserDto dto, AppDbContext db) =>
{
    var admin = await GetUserFromRequest(req, db);
    if (admin == null || admin.Role != "Admin") return Results.Unauthorized();

    if (dto == null || string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Email))
    {
        return Results.BadRequest(new { error = "Username and email are required" });
    }

    if (await db.Users.AnyAsync(u => u.Username == dto.Username))
    {
        return Results.Conflict(new { error = "Username already exists" });
    }

    var tmp = PasswordHelper.GenerateTemporaryPassword(10);
    var user = new User
    {
        Username = dto.Username,
        DisplayName = dto.DisplayName,
        Email = dto.Email,
        Role = string.IsNullOrWhiteSpace(dto.Role) ? "Employee" : dto.Role,
        PasswordHash = PasswordHelper.HashPassword(tmp),
        PasswordSetAt = DateTime.UtcNow,
        ForcePasswordChange = true,
        Theme = "light"
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    _ = Task.Run(async () =>
    {
        try
        {
            var emailService = new EmailService();
            await emailService.SendTemporaryPasswordAsync(user.Email ?? "", user.Username, tmp);
        }
        catch
        {
        }
    });

    return Results.Created($"/users/{user.Username}", new { username = user.Username, displayName = user.DisplayName, email = user.Email, role = user.Role });
});

app.MapDelete("/users/{username}", async (HttpRequest req, string username, AppDbContext db) =>
{
    var admin = await GetUserFromRequest(req, db);
    if (admin == null || admin.Role != "Admin") return Results.Unauthorized();

    var target = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (target == null) return Results.NotFound();

    if (target.Username == admin.Username)
    {
        return Results.BadRequest(new { error = "Cannot delete yourself" });
    }

    if (target.Role == "Admin")
    {
        var adminsCount = await db.Users.CountAsync(u => u.Role == "Admin");
        if (adminsCount <= 1)
        {
            return Results.BadRequest(new { error = "Cannot delete the last admin" });
        }
    }

    db.Users.Remove(target);
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Deleted", username });
});

app.MapGet("/requests/mine", async (HttpRequest req, AppDbContext db) =>
{
    var user = await GetUserFromRequest(req, db);
    if (user == null) return Results.Unauthorized();

    var mine = await db.Requests
        .Where(r => r.EmployeeUsername == user.Username)
        .OrderBy(r => r.Date)
        .ToListAsync();

    return Results.Ok(mine);
});

app.MapPost("/requests", async (HttpRequest req, CreateRequestDto dto, AppDbContext db) =>
{
    var user = await GetUserFromRequest(req, db);
    if (user == null) return Results.Unauthorized();
    if (dto == null) return Results.BadRequest();

    var id = await GetNextIdAsync(db);
    var request = new Request
    {
        Id = id,
        EmployeeUsername = user.Username,
        EmployeeName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
        Date = dto.Date.Date,
        Status = "Pending"
    };

    db.Requests.Add(request);
    await db.SaveChangesAsync();

    _ = Task.Run(async () =>
    {
        var emailService = new EmailService();
        await emailService.SendRequestNotificationAsync("paolo.bini@fos.it", request.EmployeeName, request.Date.ToString("yyyy-MM-dd"));
    });

    return Results.Created($"/requests/{request.Id}", request);
});

app.MapPost("/requests/{id}/approve", async (HttpRequest req, int id, AppDbContext db) =>
{
    var user = await GetUserFromRequest(req, db);
    if (user == null || user.Role != "Admin") return Results.Unauthorized();

    var request = await db.Requests.FirstOrDefaultAsync(x => x.Id == id);
    if (request == null) return Results.NotFound();

    request.Status = "Approved";
    request.DecisionBy = user.Username;
    request.DecisionAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    _ = Task.Run(async () =>
    {
        try
        {
            await using var scopeDb = CreateDbContext(connectionString);
            var employee = await scopeDb.Users.FirstOrDefaultAsync(u => u.Username == request.EmployeeUsername);
            if (employee != null)
            {
                var emailService = new EmailService();
                await emailService.SendDecisionNotificationAsync(employee.Email ?? "paolo.bini@fos.it", request.EmployeeName, request.Date.ToString("yyyy-MM-dd"), true, user.Username);
            }
        }
        catch
        {
        }
    });

    return Results.Ok(request);
});

app.MapPost("/requests/{id}/reject", async (HttpRequest req, int id, AppDbContext db) =>
{
    var user = await GetUserFromRequest(req, db);
    if (user == null || user.Role != "Admin") return Results.Unauthorized();

    var request = await db.Requests.FirstOrDefaultAsync(x => x.Id == id);
    if (request == null) return Results.NotFound();

    request.Status = "Rejected";
    request.DecisionBy = user.Username;
    request.DecisionAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    _ = Task.Run(async () =>
    {
        try
        {
            await using var scopeDb = CreateDbContext(connectionString);
            var employee = await scopeDb.Users.FirstOrDefaultAsync(u => u.Username == request.EmployeeUsername);
            if (employee != null)
            {
                var emailService = new EmailService();
                await emailService.SendDecisionNotificationAsync(employee.Email ?? "paolo.bini@fos.it", request.EmployeeName, request.Date.ToString("yyyy-MM-dd"), false, user.Username);
            }
        }
        catch
        {
        }
    });

    return Results.Ok(request);
});

app.MapDelete("/requests/{id}", async (HttpRequest req, int id, AppDbContext db) =>
{
    var user = await GetUserFromRequest(req, db);
    if (user == null) return Results.Unauthorized();

    var request = await db.Requests.FirstOrDefaultAsync(x => x.Id == id);
    if (request == null) return Results.NotFound();
    if (request.EmployeeUsername != user.Username && user.Role != "Admin") return Results.Forbid();

    db.Requests.Remove(request);
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Deleted" });
});

app.MapGet("/recurring-requests", async (HttpRequest req, AppDbContext db) =>
{
    var user = await GetUserFromRequest(req, db);
    if (user == null || user.Role != "Admin") return Results.Unauthorized();

    var list = await db.RecurringRequests.OrderBy(r => r.DayOfWeek).ToListAsync();
    return Results.Ok(list);
});

app.MapGet("/recurring-requests/mine", async (HttpRequest req, AppDbContext db) =>
{
    var user = await GetUserFromRequest(req, db);
    if (user == null) return Results.Unauthorized();

    var mine = await db.RecurringRequests
        .Where(r => r.EmployeeUsername == user.Username)
        .OrderBy(r => r.DayOfWeek)
        .ToListAsync();

    return Results.Ok(mine);
});

app.MapPost("/recurring-requests", async (HttpRequest req, CreateRecurringRequestDto dto, AppDbContext db) =>
{
    var user = await GetUserFromRequest(req, db);
    if (user == null) return Results.Unauthorized();
    if (dto == null || dto.DayOfWeek < 0 || dto.DayOfWeek > 6) return Results.BadRequest();

    var id = await GetNextIdAsync(db);
    var request = new RecurringRequest
    {
        Id = id,
        EmployeeUsername = user.Username,
        EmployeeName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
        DayOfWeek = dto.DayOfWeek,
        DayName = dto.DayName,
        Status = "Pending"
    };

    db.RecurringRequests.Add(request);
    await db.SaveChangesAsync();

    _ = Task.Run(async () =>
    {
        var emailService = new EmailService();
        await emailService.SendRequestNotificationAsync("paolo.bini@fos.it", request.EmployeeName, $"ogni {request.DayName}");
    });

    return Results.Created($"/recurring-requests/{request.Id}", request);
});

app.MapPost("/recurring-requests/{id}/approve", async (HttpRequest req, int id, AppDbContext db) =>
{
    var user = await GetUserFromRequest(req, db);
    if (user == null || user.Role != "Admin") return Results.Unauthorized();

    var request = await db.RecurringRequests.FirstOrDefaultAsync(x => x.Id == id);
    if (request == null) return Results.NotFound();

    request.Status = "Approved";
    request.DecisionBy = user.Username;
    request.DecisionAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    _ = Task.Run(async () =>
    {
        try
        {
            await using var scopeDb = CreateDbContext(connectionString);
            var employee = await scopeDb.Users.FirstOrDefaultAsync(u => u.Username == request.EmployeeUsername);
            if (employee != null)
            {
                var emailService = new EmailService();
                await emailService.SendDecisionNotificationAsync(employee.Email ?? "paolo.bini@fos.it", request.EmployeeName, $"ogni {request.DayName}", true, user.Username);
            }
        }
        catch
        {
        }
    });

    return Results.Ok(request);
});

app.MapPost("/recurring-requests/{id}/reject", async (HttpRequest req, int id, AppDbContext db) =>
{
    var user = await GetUserFromRequest(req, db);
    if (user == null || user.Role != "Admin") return Results.Unauthorized();

    var request = await db.RecurringRequests.FirstOrDefaultAsync(x => x.Id == id);
    if (request == null) return Results.NotFound();

    request.Status = "Rejected";
    request.DecisionBy = user.Username;
    request.DecisionAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    _ = Task.Run(async () =>
    {
        try
        {
            await using var scopeDb = CreateDbContext(connectionString);
            var employee = await scopeDb.Users.FirstOrDefaultAsync(u => u.Username == request.EmployeeUsername);
            if (employee != null)
            {
                var emailService = new EmailService();
                await emailService.SendDecisionNotificationAsync(employee.Email ?? "paolo.bini@fos.it", request.EmployeeName, $"ogni {request.DayName}", false, user.Username);
            }
        }
        catch
        {
        }
    });

    return Results.Ok(request);
});

app.MapDelete("/recurring-requests/{id}", async (HttpRequest req, int id, AppDbContext db) =>
{
    var user = await GetUserFromRequest(req, db);
    if (user == null) return Results.Unauthorized();

    var request = await db.RecurringRequests.FirstOrDefaultAsync(x => x.Id == id);
    if (request == null) return Results.NotFound();
    if (request.EmployeeUsername != user.Username && user.Role != "Admin") return Results.Forbid();

    db.RecurringRequests.Remove(request);
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Deleted" });
});

app.Run();

async Task<User?> GetUserFromRequest(HttpRequest req, AppDbContext db)
{
    if (!req.Headers.TryGetValue("Authorization", out var val)) return null;
    var s = val.ToString();
    if (!s.StartsWith("Bearer ")) return null;

    var token = s.Substring("Bearer ".Length).Trim();
    if (!sessions.TryGetValue(token, out var username)) return null;

    return await db.Users.FirstOrDefaultAsync(u => u.Username == username);
}

static async Task<int> GetNextIdAsync(AppDbContext db)
{
    var maxRequestId = await db.Requests.Select(x => (int?)x.Id).MaxAsync() ?? 0;
    var maxRecurringId = await db.RecurringRequests.Select(x => (int?)x.Id).MaxAsync() ?? 0;
    return Math.Max(maxRequestId, maxRecurringId) + 1;
}

static AppDbContext CreateDbContext(string connectionString)
{
    var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(connectionString)
        .Options;
    return new AppDbContext(options);
}

static string NormalizeDatabaseUrl(string databaseUrl)
{
    if (databaseUrl.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))
    {
        return databaseUrl;
    }

    if (!databaseUrl.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        && !databaseUrl.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("DATABASE_URL format not supported. Use postgres://... or Host=... format.");
    }

    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':', 2);
    if (userInfo.Length != 2)
    {
        throw new InvalidOperationException("DATABASE_URL is missing username or password.");
    }

    var username = Uri.UnescapeDataString(userInfo[0]);
    var password = Uri.UnescapeDataString(userInfo[1]);
    var database = uri.AbsolutePath.Trim('/');

    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port,
        Database = database,
        Username = username,
        Password = password,
        SslMode = SslMode.Require,
        TrustServerCertificate = true
    };

    return builder.ConnectionString;
}

static void LogDbConnectionAttempt(string connectionString, string phase)
{
    try
    {
        var csb = new NpgsqlConnectionStringBuilder(connectionString);
        Console.WriteLine($"[DB {phase}] host={csb.Host};port={csb.Port};db={csb.Database};user={csb.Username};ssl={csb.SslMode};timeout={csb.Timeout}s");
    }
    catch
    {
        Console.WriteLine($"[DB {phase}] unable to parse connection string");
    }
}

static void TryOpenDbConnection(string connectionString)
{
    try
    {
        using var conn = new NpgsqlConnection(connectionString);
        conn.Open();
        Console.WriteLine("[DB startup-check] connection opened successfully");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB startup-check ERROR] {ex.GetType().Name}: {ex.Message}");
        if (ex is PostgresException pg)
        {
            Console.WriteLine($"[DB startup-check ERROR] sqlstate={pg.SqlState}; severity={pg.Severity}; detail={pg.Detail}; hint={pg.Hint}");
        }
        if (ex is NpgsqlException npg && npg.InnerException != null)
        {
            Console.WriteLine($"[DB startup-check ERROR] inner={npg.InnerException.GetType().Name}: {npg.InnerException.Message}");
        }
        if (ex.InnerException is SocketException sock)
        {
            Console.WriteLine($"[DB startup-check ERROR] socket={sock.SocketErrorCode}");
        }
        throw;
    }
}
