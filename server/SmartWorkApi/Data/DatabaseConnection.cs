using System.Net.Sockets;
using Npgsql;

public static class DatabaseConnection
{
    public static string NormalizeDatabaseUrl(string databaseUrl)
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

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port,
            Database = uri.AbsolutePath.Trim('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = Uri.UnescapeDataString(userInfo[1]),
            SslMode = SslMode.Require,
            TrustServerCertificate = true
        };

        return builder.ConnectionString;
    }

    public static void LogDbConnectionAttempt(string connectionString, string phase)
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

    public static void TryOpenDbConnection(string connectionString)
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
}