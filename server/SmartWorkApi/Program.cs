using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCors();

var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (string.IsNullOrWhiteSpace(databaseUrl))
{
    throw new InvalidOperationException("DATABASE_URL environment variable is required.");
}

var connectionString = DatabaseConnection.NormalizeDatabaseUrl(databaseUrl);
DatabaseConnection.LogDbConnectionAttempt(connectionString, "startup-check");
DatabaseConnection.TryOpenDbConnection(connectionString);

// DbContext pooling reduces allocations under load.
builder.Services.AddDbContextPool<AppDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<AuthDomainService>();
builder.Services.AddScoped<UserDomainService>();
builder.Services.AddScoped<RequestDomainService>();
builder.Services.AddScoped<RecurringRequestDomainService>();

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

app.MapGet("/", () => Results.Ok(new { message = "SmartWork API" }));

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapUserEndpoints();
app.MapRequestEndpoints();
app.MapRecurringRequestEndpoints();

app.Run();
