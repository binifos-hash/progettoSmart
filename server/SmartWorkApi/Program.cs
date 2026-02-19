using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors();
var app = builder.Build();

// Bind to all interfaces (0.0.0.0) for cloud deployment; read PORT from environment
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Urls.Add($"http://0.0.0.0:{port}");

app.UseCors(policy => policy
    .WithOrigins("http://localhost:5173", "http://localhost:5174")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials());

// Read database connection from environment or use local SQLite for development
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
string dbPath;
if (!string.IsNullOrEmpty(databaseUrl) && databaseUrl.StartsWith("postgresql"))
{
    // When using Postgres, we don't use local file path
    dbPath = "postgres"; // marker - will be handled by DataStore logic if needed
}
else
{
    dbPath = Path.Combine(AppContext.BaseDirectory, "data.db");
}
var store = DataStore.LoadOrCreate(dbPath);
var sessions = new ConcurrentDictionary<string, string>(); // token -> username

app.MapGet("/", () => Results.Ok(new { message = "SmartWork API" }));

app.MapPost("/auth/login", (LoginDto dto) =>
{
    if (dto == null) return Results.BadRequest();
    var user = store.Users.FirstOrDefault(u => u.Username == dto.Username);
    if (user == null) return Results.Unauthorized();

    // Verify password (supports legacy plain Password field)
    bool verified = false;
    if (!string.IsNullOrWhiteSpace(user.PasswordHash)) {
        verified = PasswordHelper.VerifyPassword(user.PasswordHash, dto.Password);
    } else if (!string.IsNullOrEmpty(user.Password)) {
        // legacy - upgrade
        if (user.Password == dto.Password) verified = true;
        if (verified) {
            user.PasswordHash = PasswordHelper.HashPassword(dto.Password);
            user.Password = null;
            user.PasswordSetAt = DateTime.UtcNow;
            user.ForcePasswordChange = true;
            store.Save(dbPath);
        }
    }
    if (!verified) return Results.Unauthorized();
    // Check password expiry (every 4 months)
    if (user.PasswordSetAt == null || user.PasswordSetAt <= DateTime.UtcNow.AddMonths(-4)) {
        user.ForcePasswordChange = true;
        store.Save(dbPath);
    }

    var token = Guid.NewGuid().ToString();
    sessions[token] = user.Username;
    return Results.Ok(new { token, username = user.Username, role = user.Role, email = user.Email, theme = user.Theme, forcePasswordChange = user.ForcePasswordChange });
});

// Forgot password - send temporary password to email
app.MapPost("/auth/forgot-password", async (ForgotDto dto) =>
{
    if (dto == null || string.IsNullOrWhiteSpace(dto.Email)) return Results.BadRequest();
    var user = store.Users.FirstOrDefault(u => string.Equals(u.Email, dto.Email, StringComparison.OrdinalIgnoreCase));
    if (user == null) return Results.NotFound();
    // generate temporary password
    var tmp = PasswordHelper.GenerateTemporaryPassword(10);
    user.PasswordHash = PasswordHelper.HashPassword(tmp);
    user.PasswordSetAt = DateTime.UtcNow;
    user.ForcePasswordChange = true;
    store.Save(dbPath);

    _ = Task.Run(async () => {
        var emailService = new EmailService();
    await emailService.SendTemporaryPasswordAsync(user.Email ?? "", user.Username, tmp);
    });
    return Results.Ok(new { message = "Temporary password sent" });
});

// Change password (requires auth). If ForcePasswordChange is true, oldPassword can be omitted.
app.MapPost("/me/change-password", (HttpRequest req, ChangePasswordDto dto) =>
{
    var user = GetUserFromRequest(req);
    if (user == null) return Results.Unauthorized();
    if (dto == null || string.IsNullOrWhiteSpace(dto.NewPassword)) return Results.BadRequest();
    if (!user.ForcePasswordChange) {
        if (string.IsNullOrWhiteSpace(dto.OldPassword)) return Results.BadRequest();
        // verify old
        bool ok = false;
        if (!string.IsNullOrWhiteSpace(user.PasswordHash)) ok = PasswordHelper.VerifyPassword(user.PasswordHash, dto.OldPassword);
        else if (!string.IsNullOrEmpty(user.Password)) ok = user.Password == dto.OldPassword;
        if (!ok) return Results.Forbid();
    }
    user.PasswordHash = PasswordHelper.HashPassword(dto.NewPassword);
    user.PasswordSetAt = DateTime.UtcNow;
    user.ForcePasswordChange = false;
    user.Password = null;
    store.Save(dbPath);
    return Results.Ok(new { message = "Password changed" });
});

app.MapGet("/me", (HttpRequest req) =>
{
    var user = GetUserFromRequest(req);
    if (user == null) return Results.Unauthorized();
    return Results.Ok(new { username = user.Username, role = user.Role, email = user.Email, theme = user.Theme });
});

app.MapPost("/me/theme", (HttpRequest req, ThemeDto dto) =>
{
    var user = GetUserFromRequest(req);
    if (user == null) return Results.Unauthorized();
    if (dto == null || string.IsNullOrWhiteSpace(dto.Theme)) return Results.BadRequest();
    user.Theme = dto.Theme;
    store.Save(dbPath);
    return Results.Ok(new { theme = user.Theme });
});

app.MapGet("/requests", (HttpRequest req) =>
{
    // admin only: return all requests
    var user = GetUserFromRequest(req);
    if (user == null || user.Role != "Admin") return Results.Unauthorized();
    return Results.Ok(store.Requests.OrderBy(r => r.Date));
});

// Users management (admin)
app.MapGet("/users", (HttpRequest req) =>
{
    var user = GetUserFromRequest(req);
    if (user == null || user.Role != "Admin") return Results.Unauthorized();
    var list = store.Users.Select(u => new { username = u.Username, displayName = u.DisplayName, email = u.Email, role = u.Role });
    return Results.Ok(list);
});

app.MapPost("/users", async (HttpRequest req, CreateUserDto dto) =>
{
    var admin = GetUserFromRequest(req);
    if (admin == null || admin.Role != "Admin") return Results.Unauthorized();
    if (dto == null || string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Email)) return Results.BadRequest(new { error = "Username and email are required" });
    if (store.Users.Any(u => u.Username == dto.Username)) return Results.Conflict(new { error = "Username already exists" });

    var tmp = PasswordHelper.GenerateTemporaryPassword(10);
    var user = new User
    {
        Username = dto.Username,
        DisplayName = dto.DisplayName,
        Email = dto.Email,
        Role = string.IsNullOrWhiteSpace(dto.Role) ? "Employee" : dto.Role,
        PasswordHash = PasswordHelper.HashPassword(tmp),
        PasswordSetAt = DateTime.UtcNow,
        ForcePasswordChange = true
    };
    store.Users.Add(user);
    store.Save(dbPath);

    _ = Task.Run(async () =>
    {
        try
        {
            var emailService = new EmailService();
            await emailService.SendTemporaryPasswordAsync(user.Email ?? "", user.Username, tmp);
        }
        catch { }
    });

    return Results.Created($"/users/{user.Username}", new { username = user.Username, displayName = user.DisplayName, email = user.Email, role = user.Role });
});

// Delete user (admin only)
app.MapDelete("/users/{username}", (HttpRequest req, string username) =>
{
    var admin = GetUserFromRequest(req);
    if (admin == null || admin.Role != "Admin") return Results.Unauthorized();
    var u = store.Users.FirstOrDefault(x => x.Username == username);
    if (u == null) return Results.NotFound();
    // Prevent deleting yourself
    if (u.Username == admin.Username) return Results.BadRequest(new { error = "Cannot delete yourself" });
    // Prevent deleting the last admin
    if (u.Role == "Admin" && store.Users.Count(x => x.Role == "Admin") <= 1)
    {
        return Results.BadRequest(new { error = "Cannot delete the last admin" });
    }
    store.Users.Remove(u);
    store.Save(dbPath);
    return Results.Ok(new { message = "Deleted", username = username });
});

app.MapGet("/requests/mine", (HttpRequest req) =>
{
    var user = GetUserFromRequest(req);
    if (user == null) return Results.Unauthorized();
    var mine = store.Requests.Where(r => r.EmployeeUsername == user.Username).OrderBy(r => r.Date);
    return Results.Ok(mine);
});

app.MapPost("/requests", async (HttpRequest req, CreateRequestDto dto) =>
{
    var user = GetUserFromRequest(req);
    if (user == null) return Results.Unauthorized();
    if (dto == null) return Results.BadRequest();
    var id = Interlocked.Increment(ref store.NextId);
    var r = new Request
    {
        Id = id,
        EmployeeUsername = user.Username,
        EmployeeName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
        Date = dto.Date.Date,
        Status = "Pending"
    };
    store.Requests.Add(r);
    store.Save(dbPath);

    // Send email to admin
    _ = Task.Run(async () =>
    {
        var emailService = new EmailService();
        await emailService.SendRequestNotificationAsync(
            "paolo.bini@fos.it",
            r.EmployeeName,
            r.Date.ToString("yyyy-MM-dd")
        );
    });

    return Results.Created($"/requests/{id}", r);
});

app.MapPost("/requests/{id}/approve", (HttpRequest req, int id) =>
{
    var user = GetUserFromRequest(req);
    if (user == null || user.Role != "Admin") return Results.Unauthorized();
    var r = store.Requests.FirstOrDefault(x => x.Id == id);
    if (r == null) return Results.NotFound();
    r.Status = "Approved";
    r.DecisionBy = user.Username;
    r.DecisionAt = DateTime.UtcNow;
    store.Save(dbPath);

    // Send email to employee notifying approval
    _ = Task.Run(async () =>
    {
        try
        {
            var employee = store.Users.FirstOrDefault(u => u.Username == r.EmployeeUsername);
            if (employee != null)
            {
                var emailService = new EmailService();
                await emailService.SendDecisionNotificationAsync(
                    employee.Email ?? "paolo.bini@fos.it",
                    r.EmployeeName,
                    r.Date.ToString("yyyy-MM-dd"),
                    true,
                    user.Username
                );
            }
        }
        catch { }
    });
    return Results.Ok(r);
});

app.MapPost("/requests/{id}/reject", (HttpRequest req, int id) =>
{
    var user = GetUserFromRequest(req);
    if (user == null || user.Role != "Admin") return Results.Unauthorized();
    var r = store.Requests.FirstOrDefault(x => x.Id == id);
    if (r == null) return Results.NotFound();
    r.Status = "Rejected";
    r.DecisionBy = user.Username;
    r.DecisionAt = DateTime.UtcNow;
    store.Save(dbPath);

    // Send email to employee notifying rejection
    _ = Task.Run(async () =>
    {
        try
        {
            var employee = store.Users.FirstOrDefault(u => u.Username == r.EmployeeUsername);
            if (employee != null)
            {
                var emailService = new EmailService();
                await emailService.SendDecisionNotificationAsync(
                    employee.Email ?? "paolo.bini@fos.it",
                    r.EmployeeName,
                    r.Date.ToString("yyyy-MM-dd"),
                    false,
                    user.Username
                );
            }
        }
        catch { }
    });
    return Results.Ok(r);
});

// Delete a single request (owner or admin)
app.MapDelete("/requests/{id}", (HttpRequest req, int id) =>
{
    var user = GetUserFromRequest(req);
    if (user == null) return Results.Unauthorized();
    var r = store.Requests.FirstOrDefault(x => x.Id == id);
    if (r == null) return Results.NotFound();
    if (r.EmployeeUsername != user.Username && user.Role != "Admin") return Results.Forbid();
    store.Requests.Remove(r);
    store.Save(dbPath);
    return Results.Ok(new { message = "Deleted" });
});

app.MapGet("/recurring-requests", (HttpRequest req) =>
{
    var user = GetUserFromRequest(req);
    if (user == null || user.Role != "Admin") return Results.Unauthorized();
    return Results.Ok(store.RecurringRequests.OrderBy(r => r.DayOfWeek));
});

app.MapGet("/recurring-requests/mine", (HttpRequest req) =>
{
    var user = GetUserFromRequest(req);
    if (user == null) return Results.Unauthorized();
    var mine = store.RecurringRequests.Where(r => r.EmployeeUsername == user.Username).OrderBy(r => r.DayOfWeek);
    return Results.Ok(mine);
});

app.MapPost("/recurring-requests", async (HttpRequest req, CreateRecurringRequestDto dto) =>
{
    var user = GetUserFromRequest(req);
    if (user == null) return Results.Unauthorized();
    if (dto == null || dto.DayOfWeek < 0 || dto.DayOfWeek > 6) return Results.BadRequest();
    
    var id = Interlocked.Increment(ref store.NextId);
    var r = new RecurringRequest
    {
        Id = id,
        EmployeeUsername = user.Username,
        EmployeeName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
        DayOfWeek = dto.DayOfWeek,
        DayName = dto.DayName,
        Status = "Pending"
    };
    store.RecurringRequests.Add(r);
    store.Save(dbPath);

    // Send email to admin
    _ = Task.Run(async () =>
    {
        var emailService = new EmailService();
        await emailService.SendRequestNotificationAsync(
            "paolo.bini@fos.it",
            r.EmployeeName,
            $"ogni {r.DayName}"
        );
    });

    return Results.Created($"/recurring-requests/{id}", r);
});

app.MapPost("/recurring-requests/{id}/approve", (HttpRequest req, int id) =>
{
    var user = GetUserFromRequest(req);
    if (user == null || user.Role != "Admin") return Results.Unauthorized();
    var r = store.RecurringRequests.FirstOrDefault(x => x.Id == id);
    if (r == null) return Results.NotFound();
    r.Status = "Approved";
    r.DecisionBy = user.Username;
    r.DecisionAt = DateTime.UtcNow;
    store.Save(dbPath);

    // Send email to employee
    _ = Task.Run(async () =>
    {
        try
        {
            var employee = store.Users.FirstOrDefault(u => u.Username == r.EmployeeUsername);
            if (employee != null)
            {
                var emailService = new EmailService();
                await emailService.SendDecisionNotificationAsync(
                    employee.Email ?? "paolo.bini@fos.it",
                    r.EmployeeName,
                    $"ogni {r.DayName}",
                    true,
                    user.Username
                );
            }
        }
        catch { }
    });
    return Results.Ok(r);
});

app.MapPost("/recurring-requests/{id}/reject", (HttpRequest req, int id) =>
{
    var user = GetUserFromRequest(req);
    if (user == null || user.Role != "Admin") return Results.Unauthorized();
    var r = store.RecurringRequests.FirstOrDefault(x => x.Id == id);
    if (r == null) return Results.NotFound();
    r.Status = "Rejected";
    r.DecisionBy = user.Username;
    r.DecisionAt = DateTime.UtcNow;
    store.Save(dbPath);

    // Send email to employee
    _ = Task.Run(async () =>
    {
        try
        {
            var employee = store.Users.FirstOrDefault(u => u.Username == r.EmployeeUsername);
            if (employee != null)
            {
                var emailService = new EmailService();
                await emailService.SendDecisionNotificationAsync(
                    employee.Email ?? "paolo.bini@fos.it",
                    r.EmployeeName,
                    $"ogni {r.DayName}",
                    false,
                    user.Username
                );
            }
        }
        catch { }
    });
    return Results.Ok(r);
});

// Delete a recurring request (owner or admin)
app.MapDelete("/recurring-requests/{id}", (HttpRequest req, int id) =>
{
    var user = GetUserFromRequest(req);
    if (user == null) return Results.Unauthorized();
    var r = store.RecurringRequests.FirstOrDefault(x => x.Id == id);
    if (r == null) return Results.NotFound();
    if (r.EmployeeUsername != user.Username && user.Role != "Admin") return Results.Forbid();
    store.RecurringRequests.Remove(r);
    store.Save(dbPath);
    return Results.Ok(new { message = "Deleted" });
});

app.Run();

User? GetUserFromRequest(HttpRequest req)
{
    if (!req.Headers.TryGetValue("Authorization", out var val)) return null;
    var s = val.ToString();
    if (!s.StartsWith("Bearer ")) return null;
    var token = s.Substring("Bearer ".Length).Trim();
    if (!sessions.TryGetValue(token, out var username)) return null;
    return store.Users.FirstOrDefault(u => u.Username == username);
}

public class Request
{
    public int Id { get; set; }
    public string EmployeeUsername { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public DateTime Date { get; set; }
    public string Status { get; set; } = "";
    public string? DecisionBy { get; set; }
    public DateTime? DecisionAt { get; set; }
}

public class CreateRequestDto
{
    public DateTime Date { get; set; }
}

public class User
{
    public string Username { get; set; } = "";
    // legacy plain password (will be upgraded on first login)
    public string? Password { get; set; }
    public string? PasswordHash { get; set; }
    public DateTime? PasswordSetAt { get; set; }
    public bool ForcePasswordChange { get; set; } = true;
    public string Role { get; set; } = "Employee"; // or Admin
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string Theme { get; set; } = "light";
}

public class DataStore
{
    public List<User> Users { get; set; } = new List<User>();
    public List<Request> Requests { get; set; } = new List<Request>();
    public List<RecurringRequest> RecurringRequests { get; set; } = new List<RecurringRequest>();
    public int NextId = 0;
    // Database-backed store (SQLite). `path` is the sqlite file path.
    public void Save(string dbPath)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var tx = conn.BeginTransaction();
            // Create tables if they don't exist
            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Users (
  Username TEXT PRIMARY KEY,
  Password TEXT,
  PasswordHash TEXT,
  PasswordSetAt TEXT,
  ForcePasswordChange INTEGER,
  Role TEXT,
  DisplayName TEXT,
  Email TEXT,
  Theme TEXT
);
CREATE TABLE IF NOT EXISTS Requests (
  Id INTEGER PRIMARY KEY,
  EmployeeUsername TEXT,
  EmployeeName TEXT,
  Date TEXT,
  Status TEXT,
  DecisionBy TEXT,
  DecisionAt TEXT
);
CREATE TABLE IF NOT EXISTS RecurringRequests (
  Id INTEGER PRIMARY KEY,
  EmployeeUsername TEXT,
  EmployeeName TEXT,
  DayOfWeek INTEGER,
  DayName TEXT,
  Status TEXT,
  DecisionBy TEXT,
  DecisionAt TEXT
);
";
            cmd.ExecuteNonQuery();

            // Clear tables and re-insert (simple approach)
            cmd.CommandText = "DELETE FROM Users; DELETE FROM Requests; DELETE FROM RecurringRequests;";
            cmd.ExecuteNonQuery();

            // Insert users
            foreach (var u in Users)
            {
                cmd.CommandText = "INSERT INTO Users (Username, Password, PasswordHash, PasswordSetAt, ForcePasswordChange, Role, DisplayName, Email, Theme) VALUES ($u, $p, $ph, $ps, $f, $r, $d, $e, $t);";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$u", u.Username);
                cmd.Parameters.AddWithValue("$p", u.Password ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$ph", u.PasswordHash ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$ps", u.PasswordSetAt?.ToString("o") ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$f", u.ForcePasswordChange ? 1 : 0);
                cmd.Parameters.AddWithValue("$r", u.Role ?? "Employee");
                cmd.Parameters.AddWithValue("$d", u.DisplayName ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$e", u.Email ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$t", u.Theme ?? (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            // Insert requests
            foreach (var r in Requests)
            {
                cmd.CommandText = "INSERT INTO Requests (Id, EmployeeUsername, EmployeeName, Date, Status, DecisionBy, DecisionAt) VALUES ($id, $u, $n, $d, $s, $db, $da);";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$id", r.Id);
                cmd.Parameters.AddWithValue("$u", r.EmployeeUsername);
                cmd.Parameters.AddWithValue("$n", r.EmployeeName ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$d", r.Date.ToString("o"));
                cmd.Parameters.AddWithValue("$s", r.Status ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$db", r.DecisionBy ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$da", r.DecisionAt?.ToString("o") ?? (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            // Insert recurring
            foreach (var rr in RecurringRequests)
            {
                cmd.CommandText = "INSERT INTO RecurringRequests (Id, EmployeeUsername, EmployeeName, DayOfWeek, DayName, Status, DecisionBy, DecisionAt) VALUES ($id, $u, $n, $dw, $dn, $s, $db, $da);";
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$id", rr.Id);
                cmd.Parameters.AddWithValue("$u", rr.EmployeeUsername);
                cmd.Parameters.AddWithValue("$n", rr.EmployeeName ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$dw", rr.DayOfWeek);
                cmd.Parameters.AddWithValue("$dn", rr.DayName ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$s", rr.Status ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$db", rr.DecisionBy ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$da", rr.DecisionAt?.ToString("o") ?? (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB SAVE ERROR] {ex.Message}");
        }
    }

    public static DataStore LoadOrCreate(string dbPath)
    {
        var ds = new DataStore();
        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Users (
  Username TEXT PRIMARY KEY,
  Password TEXT,
  PasswordHash TEXT,
  PasswordSetAt TEXT,
  ForcePasswordChange INTEGER,
  Role TEXT,
  DisplayName TEXT,
  Email TEXT,
  Theme TEXT
);
CREATE TABLE IF NOT EXISTS Requests (
  Id INTEGER PRIMARY KEY,
  EmployeeUsername TEXT,
  EmployeeName TEXT,
  Date TEXT,
  Status TEXT,
  DecisionBy TEXT,
  DecisionAt TEXT
);
CREATE TABLE IF NOT EXISTS RecurringRequests (
  Id INTEGER PRIMARY KEY,
  EmployeeUsername TEXT,
  EmployeeName TEXT,
  DayOfWeek INTEGER,
  DayName TEXT,
  Status TEXT,
  DecisionBy TEXT,
  DecisionAt TEXT
);
";
            cmd.ExecuteNonQuery();

            // Load users
            cmd.CommandText = "SELECT Username, Password, PasswordHash, PasswordSetAt, ForcePasswordChange, Role, DisplayName, Email, Theme FROM Users";
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                var u = new User
                {
                    Username = rdr.GetString(0),
                    Password = rdr.IsDBNull(1) ? null : rdr.GetString(1),
                    PasswordHash = rdr.IsDBNull(2) ? null : rdr.GetString(2),
                    PasswordSetAt = rdr.IsDBNull(3) ? null : DateTime.Parse(rdr.GetString(3)),
                    ForcePasswordChange = !rdr.IsDBNull(4) && rdr.GetInt32(4) == 1,
                    Role = rdr.IsDBNull(5) ? "Employee" : rdr.GetString(5),
                    DisplayName = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                    Email = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                    Theme = rdr.IsDBNull(8) ? "light" : rdr.GetString(8)
                };
                ds.Users.Add(u);
            }

            // Load requests
            cmd.CommandText = "SELECT Id, EmployeeUsername, EmployeeName, Date, Status, DecisionBy, DecisionAt FROM Requests";
            using var rdr2 = cmd.ExecuteReader();
            while (rdr2.Read())
            {
                var r = new Request
                {
                    Id = rdr2.GetInt32(0),
                    EmployeeUsername = rdr2.IsDBNull(1) ? "" : rdr2.GetString(1),
                    EmployeeName = rdr2.IsDBNull(2) ? null : rdr2.GetString(2),
                    Date = DateTime.Parse(rdr2.GetString(3)),
                    Status = rdr2.IsDBNull(4) ? null : rdr2.GetString(4),
                    DecisionBy = rdr2.IsDBNull(5) ? null : rdr2.GetString(5),
                    DecisionAt = rdr2.IsDBNull(6) ? null : DateTime.Parse(rdr2.GetString(6))
                };
                ds.Requests.Add(r);
            }

            // Load recurring
            cmd.CommandText = "SELECT Id, EmployeeUsername, EmployeeName, DayOfWeek, DayName, Status, DecisionBy, DecisionAt FROM RecurringRequests";
            using var rdr3 = cmd.ExecuteReader();
            while (rdr3.Read())
            {
                var rr = new RecurringRequest
                {
                    Id = rdr3.GetInt32(0),
                    EmployeeUsername = rdr3.IsDBNull(1) ? "" : rdr3.GetString(1),
                    EmployeeName = rdr3.IsDBNull(2) ? null : rdr3.GetString(2),
                    DayOfWeek = rdr3.IsDBNull(3) ? 0 : rdr3.GetInt32(3),
                    DayName = rdr3.IsDBNull(4) ? null : rdr3.GetString(4),
                    Status = rdr3.IsDBNull(5) ? null : rdr3.GetString(5),
                    DecisionBy = rdr3.IsDBNull(6) ? null : rdr3.GetString(6),
                    DecisionAt = rdr3.IsDBNull(7) ? null : DateTime.Parse(rdr3.GetString(7))
                };
                ds.RecurringRequests.Add(rr);
            }

            // Compute NextId
            ds.NextId = ds.Requests.Any() ? ds.Requests.Max(r => r.Id) : 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB LOAD ERROR] {ex.Message}");
        }

        EnsureDefaults(ds);
        // Save initial state to DB if needed
        ds.Save(dbPath);
        return ds;
    }

    private static void EnsureDefaults(DataStore ds)
    {
        if (!ds.Users.Any(u => u.Username == "admin"))
        {
            var tmp = PasswordHelper.GenerateTemporaryPassword(10);
            ds.Users.Add(new User { Username = "admin", PasswordHash = PasswordHelper.HashPassword(tmp), Role = "Admin", DisplayName = "Administrator", Email = "paolo.bini@fos.it", PasswordSetAt = DateTime.UtcNow, ForcePasswordChange = true });
            Console.WriteLine($"[INIT] admin temporary password: {tmp}");
        }
        if (!ds.Users.Any(u => u.Username == "dipendente"))
        {
            var tmp = PasswordHelper.GenerateTemporaryPassword(10);
            ds.Users.Add(new User { Username = "dipendente", PasswordHash = PasswordHelper.HashPassword(tmp), Role = "Employee", DisplayName = "Dipendente", Email = "paolo.bini@fos.it", PasswordSetAt = DateTime.UtcNow, ForcePasswordChange = true });
            Console.WriteLine($"[INIT] dipendente temporary password: {tmp}");
        }
        if (ds.NextId <= 0)
        {
            ds.NextId = ds.Requests.Any() ? ds.Requests.Max(r => r.Id) : 0;
        }
        // Ensure existing users have an email set
        foreach (var u in ds.Users)
        {
            if (string.IsNullOrWhiteSpace(u.Email)) u.Email = "paolo.bini@fos.it";
            if (string.IsNullOrWhiteSpace(u.Theme)) u.Theme = "light";
            // Upgrade legacy plain passwords to hashed and require password change
            if (!string.IsNullOrEmpty(u.Password) && string.IsNullOrWhiteSpace(u.PasswordHash))
            {
                u.PasswordHash = PasswordHelper.HashPassword(u.Password);
                u.PasswordSetAt = DateTime.UtcNow;
                u.ForcePasswordChange = true;
                u.Password = null;
            }
        }
    }
}

public class LoginDto
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class ThemeDto
{
    public string Theme { get; set; } = "light";
}

public class RecurringRequest
{
    public int Id { get; set; }
    public string EmployeeUsername { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public int DayOfWeek { get; set; } // 0=Sunday, 1=Monday, ..., 6=Saturday
    public string DayName { get; set; } = "";
    public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected
    public string? DecisionBy { get; set; }
    public DateTime? DecisionAt { get; set; }
}

public class CreateRecurringRequestDto
{
    public int DayOfWeek { get; set; }
    public string DayName { get; set; } = "";
}

public class ForgotDto { public string Email { get; set; } = ""; }

public class ChangePasswordDto { public string? OldPassword { get; set; } public string NewPassword { get; set; } = ""; }

public class CreateUserDto { public string Username { get; set; } = ""; public string? DisplayName { get; set; } public string Email { get; set; } = ""; public string? Role { get; set; } }

public static class PasswordHelper
{
    public static string HashPassword(string password)
    {
        using var rng = RandomNumberGenerator.Create();
        var salt = new byte[16];
        rng.GetBytes(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(32);
        var combined = new byte[salt.Length + hash.Length];
        Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
        Buffer.BlockCopy(hash, 0, combined, salt.Length, hash.Length);
        return Convert.ToBase64String(combined);
    }

    public static bool VerifyPassword(string storedBase64, string password)
    {
        try {
            var combined = Convert.FromBase64String(storedBase64);
            var salt = new byte[16];
            Buffer.BlockCopy(combined, 0, salt, 0, salt.Length);
            var hash = new byte[32];
            Buffer.BlockCopy(combined, salt.Length, hash, 0, hash.Length);
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256);
            var testHash = pbkdf2.GetBytes(32);
            return testHash.SequenceEqual(hash);
        } catch { return false; }
    }

    public static string GenerateTemporaryPassword(int len = 10)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var sb = new StringBuilder(len);
        using var rng = RandomNumberGenerator.Create();
        var buf = new byte[4];
        for (int i = 0; i < len; i++) {
            rng.GetBytes(buf);
            var v = BitConverter.ToUInt32(buf, 0);
            sb.Append(chars[(int)(v % (uint)chars.Length)]);
        }
        return sb.ToString();
    }
}

public class EmailService
{
    public async Task SendRequestNotificationAsync(string toEmail, string employeeName, string dateString)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("SmartWork", "noreply@smartwork.local"));
            message.To.Add(new MailboxAddress("Admin", toEmail));
            message.Subject = "Nuova Richiesta di Smart Working";

            var bodyBuilder = new BodyBuilder();
            bodyBuilder.TextBody = $@"Una nuova richiesta di smart working è stata creata.

Dipendente: {employeeName}
Data: {dateString}

Accedi al sistema per revisione e approvazione.";
            message.Body = bodyBuilder.ToMessageBody();

            using (var client = new SmtpClient())
            {
                var host = "smtp.gmail.com";
                var port = 587;
                var username = Environment.GetEnvironmentVariable("SMTP_USERNAME") ?? throw new InvalidOperationException("SMTP_USERNAME environment variable is required");
                var password = Environment.GetEnvironmentVariable("SMTP_PASSWORD") ?? throw new InvalidOperationException("SMTP_PASSWORD environment variable is required");

                await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(username, password);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                Console.WriteLine($"[EMAIL] Notification sent to {toEmail}");
            }
        }
        catch (Exception ex)
        {
            // Log but don't throw - we don't want email errors to break request submission
            Console.WriteLine($"[EMAIL ERROR] Failed to send notification: {ex.Message}");
        }
    }

    public async Task SendDecisionNotificationAsync(string toEmail, string employeeName, string dateString, bool approved, string decidedBy)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("SmartWork", "noreply@smartwork.local"));
            message.To.Add(new MailboxAddress(employeeName, toEmail));
            message.Subject = approved ? "Richiesta approvata" : "Richiesta rifiutata";

            var bodyBuilder = new BodyBuilder();
            bodyBuilder.TextBody = approved
                ? $"La tua richiesta di smart working per il {dateString} è stata approvata da {decidedBy}."
                : $"La tua richiesta di smart working per il {dateString} è stata rifiutata da {decidedBy}.";
            message.Body = bodyBuilder.ToMessageBody();

            using (var client = new SmtpClient())
            {
                var host = "smtp.gmail.com";
                var port = 587;
                var username = Environment.GetEnvironmentVariable("SMTP_USERNAME") ?? throw new InvalidOperationException("SMTP_USERNAME environment variable is required");
                var password = Environment.GetEnvironmentVariable("SMTP_PASSWORD") ?? throw new InvalidOperationException("SMTP_PASSWORD environment variable is required");

                await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(username, password);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                Console.WriteLine($"[EMAIL] Decision notification sent to {toEmail}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EMAIL ERROR] Failed to send decision notification: {ex.Message}");
        }
    }

    public async Task SendTemporaryPasswordAsync(string toEmail, string username, string tempPassword)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("SmartWork", "noreply@smartwork.local"));
            message.To.Add(new MailboxAddress(username, toEmail));
            message.Subject = "Password temporanea SmartWork";

            var bodyBuilder = new BodyBuilder();
            bodyBuilder.TextBody = $@"Hai richiesto il ripristino password.

Nome utente: {username}
Password temporanea: {tempPassword}

Effettua l'accesso e cambia la password al più presto.";
            message.Body = bodyBuilder.ToMessageBody();

            using (var client = new SmtpClient())
            {
                var host = "smtp.gmail.com";
                var port = 587;
                var smtpUsername = Environment.GetEnvironmentVariable("SMTP_USERNAME") ?? throw new InvalidOperationException("SMTP_USERNAME environment variable is required");
                var smtpPassword = Environment.GetEnvironmentVariable("SMTP_PASSWORD") ?? throw new InvalidOperationException("SMTP_PASSWORD environment variable is required");

                await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(smtpUsername, smtpPassword);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                Console.WriteLine($"[EMAIL] Temporary password sent to {toEmail}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EMAIL ERROR] Failed to send temporary password: {ex.Message}");
        }
    }
}
