using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

public class EmailService
{
    private readonly ILogger<EmailService> _logger;

    public EmailService(ILogger<EmailService> logger)
    {
        _logger = logger;
    }

    private sealed class SmtpSettings
    {
        public string Host { get; init; } = "smtp.gmail.com";
        public int Port { get; init; } = 587;
        public SecureSocketOptions SecureSocketOptions { get; init; } = SecureSocketOptions.StartTls;
        public int TimeoutMs { get; init; } = 15000;
    }

    private static (string Username, string Password) GetCredentials()
    {
        var username = Environment.GetEnvironmentVariable("SMTP_USERNAME")
            ?? throw new InvalidOperationException("SMTP_USERNAME environment variable is required");

        var password = Environment.GetEnvironmentVariable("SMTP_PASSWORD")
            ?? throw new InvalidOperationException("SMTP_PASSWORD environment variable is required");

        return (username, password);
    }

    private static SmtpSettings GetSmtpSettings()
    {
        var host = Environment.GetEnvironmentVariable("SMTP_HOST");
        var portRaw = Environment.GetEnvironmentVariable("SMTP_PORT");
        var secureRaw = Environment.GetEnvironmentVariable("SMTP_SECURE");
        var timeoutRaw = Environment.GetEnvironmentVariable("SMTP_TIMEOUT_MS");

        var port = 587;
        if (!string.IsNullOrWhiteSpace(portRaw) && int.TryParse(portRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort))
        {
            port = parsedPort;
        }

        var timeoutMs = 15000;
        if (!string.IsNullOrWhiteSpace(timeoutRaw) && int.TryParse(timeoutRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTimeout))
        {
            timeoutMs = parsedTimeout;
        }

        return new SmtpSettings
        {
            Host = string.IsNullOrWhiteSpace(host) ? "smtp.gmail.com" : host,
            Port = port,
            TimeoutMs = timeoutMs,
            SecureSocketOptions = ParseSecureSocketOptions(secureRaw)
        };
    }

    private static SecureSocketOptions ParseSecureSocketOptions(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return SecureSocketOptions.StartTls;

        return value.Trim().ToLowerInvariant() switch
        {
            "auto" => SecureSocketOptions.Auto,
            "ssl" => SecureSocketOptions.SslOnConnect,
            "sslonconnect" => SecureSocketOptions.SslOnConnect,
            "starttls" => SecureSocketOptions.StartTls,
            "starttlswhenavailable" => SecureSocketOptions.StartTlsWhenAvailable,
            "none" => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls
        };
    }

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(empty)";

        var at = email.IndexOf('@');
        if (at <= 1 || at == email.Length - 1) return "***";

        var name = email[..at];
        var domain = email[(at + 1)..];
        var visibleName = name.Length <= 2 ? name[0].ToString() : $"{name[0]}***{name[^1]}";
        return $"{visibleName}@{domain}";
    }

    private void LogSmtpConfiguration(string operationId, SmtpSettings settings, string username)
    {
        var hostFromEnv = Environment.GetEnvironmentVariable("SMTP_HOST");
        var portFromEnv = Environment.GetEnvironmentVariable("SMTP_PORT");
        var secureFromEnv = Environment.GetEnvironmentVariable("SMTP_SECURE");
        var timeoutFromEnv = Environment.GetEnvironmentVariable("SMTP_TIMEOUT_MS");
        var usernameFromEnv = Environment.GetEnvironmentVariable("SMTP_USERNAME");
        var hasPassword = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMTP_PASSWORD"));

        _logger.LogInformation(
            "[EMAIL:{OperationId}] SMTP config resolved. host={Host} port={Port} secure={Secure} timeoutMs={TimeoutMs} smtpUser={SmtpUser}",
            operationId,
            settings.Host,
            settings.Port,
            settings.SecureSocketOptions,
            settings.TimeoutMs,
            MaskEmail(username));

        _logger.LogInformation(
            "[EMAIL:{OperationId}] SMTP env raw values. SMTP_HOST='{HostRaw}' SMTP_PORT='{PortRaw}' SMTP_SECURE='{SecureRaw}' SMTP_TIMEOUT_MS='{TimeoutRaw}' SMTP_USERNAME='{UserRaw}' SMTP_PASSWORD_SET={HasPassword}",
            operationId,
            string.IsNullOrWhiteSpace(hostFromEnv) ? "(null/empty -> default)" : hostFromEnv,
            string.IsNullOrWhiteSpace(portFromEnv) ? "(null/empty -> default)" : portFromEnv,
            string.IsNullOrWhiteSpace(secureFromEnv) ? "(null/empty -> default)" : secureFromEnv,
            string.IsNullOrWhiteSpace(timeoutFromEnv) ? "(null/empty -> default)" : timeoutFromEnv,
            string.IsNullOrWhiteSpace(usernameFromEnv) ? "(null/empty)" : MaskEmail(usernameFromEnv),
            hasPassword);
    }

    private async Task SendEmailAsync(MimeMessage message, string username, string password, string operationId)
    {
        var settings = GetSmtpSettings();
        LogSmtpConfiguration(operationId, settings, username);
        await LogNetworkDiagnosticsAsync(operationId, settings);

        var totalSw = Stopwatch.StartNew();

        using var client = new SmtpClient
        {
            Timeout = settings.TimeoutMs
        };

        _logger.LogInformation(
            "[EMAIL:{OperationId}] Starting SMTP send. from={From} to={To} subject='{Subject}'",
            operationId,
            string.Join(",", message.From.Mailboxes.Select(m => MaskEmail(m.Address))),
            string.Join(",", message.To.Mailboxes.Select(m => MaskEmail(m.Address))),
            message.Subject);

        try
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInformation("[EMAIL:{OperationId}] Connecting to SMTP server...", operationId);
            await client.ConnectAsync(settings.Host, settings.Port, settings.SecureSocketOptions);
            sw.Stop();
            _logger.LogInformation(
                "[EMAIL:{OperationId}] SMTP connect completed in {ElapsedMs}ms. connected={Connected} secure={Secure} authMechanisms={AuthMechanisms}",
                operationId,
                sw.ElapsedMilliseconds,
                client.IsConnected,
                client.IsSecure,
                string.Join(",", client.AuthenticationMechanisms));

            sw.Restart();
            _logger.LogInformation("[EMAIL:{OperationId}] Authenticating as {User}...", operationId, MaskEmail(username));
            await client.AuthenticateAsync(username, password);
            sw.Stop();
            _logger.LogInformation(
                "[EMAIL:{OperationId}] SMTP auth completed in {ElapsedMs}ms. authenticated={Authenticated}",
                operationId,
                sw.ElapsedMilliseconds,
                client.IsAuthenticated);

            sw.Restart();
            _logger.LogInformation("[EMAIL:{OperationId}] Sending message data...", operationId);
            await client.SendAsync(message);
            sw.Stop();
            _logger.LogInformation("[EMAIL:{OperationId}] SMTP send completed in {ElapsedMs}ms.", operationId, sw.ElapsedMilliseconds);

            sw.Restart();
            _logger.LogInformation("[EMAIL:{OperationId}] Disconnecting from SMTP server...", operationId);
            await client.DisconnectAsync(true);
            sw.Stop();
            totalSw.Stop();
            _logger.LogInformation("[EMAIL:{OperationId}] SMTP disconnect completed in {ElapsedMs}ms. TOTAL={TotalMs}ms.", operationId, sw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds);
        }
        catch
        {
            totalSw.Stop();
            _logger.LogError(
                "[EMAIL:{OperationId}] SMTP send failed after {ElapsedMs}ms. connected={Connected} authenticated={Authenticated}",
                operationId,
                totalSw.ElapsedMilliseconds,
                client.IsConnected,
                client.IsAuthenticated);
            throw;
        }
    }

    private async Task LogNetworkDiagnosticsAsync(string operationId, SmtpSettings settings)
    {
        try
        {
            _logger.LogInformation("[EMAIL:{OperationId}] Resolving SMTP host DNS... host={Host}", operationId, settings.Host);
            var dnsSw = Stopwatch.StartNew();
            var addresses = await Dns.GetHostAddressesAsync(settings.Host);
            dnsSw.Stop();

            var printableAddresses = addresses.Length == 0
                ? "(none)"
                : string.Join(",", addresses.Select(a => $"{a}({a.AddressFamily})"));

            _logger.LogInformation(
                "[EMAIL:{OperationId}] DNS resolved in {ElapsedMs}ms. addressCount={AddressCount} addresses={Addresses}",
                operationId,
                dnsSw.ElapsedMilliseconds,
                addresses.Length,
                printableAddresses);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[EMAIL:{OperationId}] DNS resolution failed for host={Host}",
                operationId,
                settings.Host);
        }

        try
        {
            using var tcpProbe = new TcpClient(AddressFamily.InterNetwork);
            using var probeCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var probeSw = Stopwatch.StartNew();

            _logger.LogInformation(
                "[EMAIL:{OperationId}] TCP probe start. host={Host} port={Port} timeoutMs={TimeoutMs}",
                operationId,
                settings.Host,
                settings.Port,
                4000);

            await tcpProbe.ConnectAsync(settings.Host, settings.Port, probeCts.Token);
            probeSw.Stop();

            _logger.LogInformation(
                "[EMAIL:{OperationId}] TCP probe OK in {ElapsedMs}ms. local={LocalEndPoint} remote={RemoteEndPoint}",
                operationId,
                probeSw.ElapsedMilliseconds,
                tcpProbe.Client.LocalEndPoint?.ToString() ?? "(null)",
                tcpProbe.Client.RemoteEndPoint?.ToString() ?? "(null)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[EMAIL:{OperationId}] TCP probe FAILED. host={Host} port={Port}",
                operationId,
                settings.Host,
                settings.Port);
        }
    }

    public async Task<bool> SendRequestNotificationAsync(string toEmail, string employeeName, string dateString)
    {
        var operationId = Guid.NewGuid().ToString("N");
        try
        {
            _logger.LogInformation(
                "[EMAIL:{OperationId}] SendRequestNotificationAsync started. to={ToEmail} employee={EmployeeName} date={DateString}",
                operationId,
                MaskEmail(toEmail),
                employeeName,
                dateString);

            var (username, password) = GetCredentials();
            _logger.LogInformation("[EMAIL:{OperationId}] SMTP credentials loaded successfully for user {SmtpUser}", operationId, MaskEmail(username));

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("SmartWork", username));
            message.To.Add(new MailboxAddress("Admin", toEmail));
            message.Subject = "Nuova Richiesta di Smart Working";
            message.Body = new BodyBuilder
            {
                TextBody = $@"Una nuova richiesta di smart working è stata creata.

Dipendente: {employeeName}
Data: {dateString}

Accedi al sistema per revisione e approvazione."
            }.ToMessageBody();

            await SendEmailAsync(message, username, password, operationId);
            _logger.LogInformation("[EMAIL:{OperationId}] SendRequestNotificationAsync completed successfully.", operationId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EMAIL:{OperationId}] Failed to send request notification. message='{Message}' inner='{Inner}'",
                operationId,
                ex.Message,
                ex.InnerException?.Message ?? "(none)");
            return false;
        }
    }

    public async Task<bool> SendDecisionNotificationAsync(string toEmail, string employeeName, string dateString, bool approved, string decidedBy)
    {
        var operationId = Guid.NewGuid().ToString("N");
        try
        {
            _logger.LogInformation(
                "[EMAIL:{OperationId}] SendDecisionNotificationAsync started. to={ToEmail} employee={EmployeeName} date={DateString} approved={Approved} decidedBy={DecidedBy}",
                operationId,
                MaskEmail(toEmail),
                employeeName,
                dateString,
                approved,
                decidedBy);

            var (username, password) = GetCredentials();
            _logger.LogInformation("[EMAIL:{OperationId}] SMTP credentials loaded successfully for user {SmtpUser}", operationId, MaskEmail(username));

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("SmartWork", username));
            message.To.Add(new MailboxAddress(employeeName, toEmail));
            message.Subject = approved ? "Richiesta approvata" : "Richiesta rifiutata";
            message.Body = new BodyBuilder
            {
                TextBody = approved
                    ? $"La tua richiesta di smart working per il {dateString} è stata approvata da {decidedBy}."
                    : $"La tua richiesta di smart working per il {dateString} è stata rifiutata da {decidedBy}."
            }.ToMessageBody();

            await SendEmailAsync(message, username, password, operationId);
            _logger.LogInformation("[EMAIL:{OperationId}] SendDecisionNotificationAsync completed successfully.", operationId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EMAIL:{OperationId}] Failed to send decision notification. message='{Message}' inner='{Inner}'",
                operationId,
                ex.Message,
                ex.InnerException?.Message ?? "(none)");
            return false;
        }
    }

    public async Task<bool> SendTemporaryPasswordAsync(string toEmail, string username, string tempPassword)
    {
        var operationId = Guid.NewGuid().ToString("N");
        try
        {
            _logger.LogInformation(
                "[EMAIL:{OperationId}] SendTemporaryPasswordAsync started. to={ToEmail} username={Username} tempPasswordLength={PwdLength}",
                operationId,
                MaskEmail(toEmail),
                username,
                string.IsNullOrEmpty(tempPassword) ? 0 : tempPassword.Length);

            var (smtpUsername, smtpPassword) = GetCredentials();
            _logger.LogInformation("[EMAIL:{OperationId}] SMTP credentials loaded successfully for user {SmtpUser}", operationId, MaskEmail(smtpUsername));

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("SmartWork", smtpUsername));
            message.To.Add(new MailboxAddress(username, toEmail));
            message.Subject = "Password temporanea SmartWork";
            message.Body = new BodyBuilder
            {
                TextBody = $@"Hai richiesto il ripristino password.

Nome utente: {username}
Password temporanea: {tempPassword}

Effettua l'accesso e cambia la password al più presto."
            }.ToMessageBody();

            await SendEmailAsync(message, smtpUsername, smtpPassword, operationId);

            _logger.LogInformation("[EMAIL:{OperationId}] SendTemporaryPasswordAsync completed successfully.", operationId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EMAIL:{OperationId}] Failed to send temporary password email. message='{Message}' inner='{Inner}'",
                operationId,
                ex.Message,
                ex.InnerException?.Message ?? "(none)");
            return false;
        }
    }
}