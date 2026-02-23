using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public class EmailService
{
    private static readonly HttpClient SendGridHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

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

    private static (string? ApiKey, string Source) GetSendGridApiKey()
    {
        var candidates = new[]
        {
            "SENDGRID_API_KEY",
            "SENDGRID_KEY",
            "SENDGRID_APIKEY"
        };

        foreach (var key in candidates)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return (value, key);
            }
        }

        return (null, "(none)");
    }

    private static string GetTransportMode()
    {
        var mode = Environment.GetEnvironmentVariable("EMAIL_TRANSPORT");
        if (string.IsNullOrWhiteSpace(mode)) return "auto";

        return mode.Trim().ToLowerInvariant() switch
        {
            "smtp" => "smtp",
            "sendgrid" => "sendgrid",
            _ => "auto"
        };
    }

    private static string ResolveFromAddress()
    {
        var from = Environment.GetEnvironmentVariable("EMAIL_FROM");
        if (!string.IsNullOrWhiteSpace(from)) return from;

        var smtpUser = Environment.GetEnvironmentVariable("SMTP_USERNAME");
        if (!string.IsNullOrWhiteSpace(smtpUser)) return smtpUser;

        throw new InvalidOperationException("EMAIL_FROM or SMTP_USERNAME environment variable is required");
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

    private static string TruncateForLog(string? value, int maxLen = 400)
    {
        if (string.IsNullOrEmpty(value)) return "(empty)";
        return value.Length <= maxLen ? value : value[..maxLen] + "...";
    }

    private static bool IsSmtpConnectTransient(Exception ex)
    {
        return ex is TimeoutException
            || ex is OperationCanceledException
            || ex is SocketException;
    }

    private static bool ShouldTryGmail465Fallback(SmtpSettings settings)
    {
        var enabledRaw = Environment.GetEnvironmentVariable("SMTP_GMAIL_465_FALLBACK");
        var enabled = string.IsNullOrWhiteSpace(enabledRaw)
            || enabledRaw.Equals("1", StringComparison.OrdinalIgnoreCase)
            || enabledRaw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || enabledRaw.Equals("yes", StringComparison.OrdinalIgnoreCase);

        return enabled
            && settings.Host.Equals("smtp.gmail.com", StringComparison.OrdinalIgnoreCase)
            && settings.Port == 587
            && settings.SecureSocketOptions == SecureSocketOptions.StartTls;
    }

    private async Task SendEmailViaSmtpWithSettingsAsync(MimeMessage message, string username, string password, string operationId, SmtpSettings settings, string attemptLabel)
    {
        _logger.LogInformation("[EMAIL:{OperationId}] SMTP attempt={AttemptLabel}", operationId, attemptLabel);
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

    private async Task SendEmailViaSmtpAsync(MimeMessage message, string username, string password, string operationId)
    {
        var primarySettings = GetSmtpSettings();

        try
        {
            await SendEmailViaSmtpWithSettingsAsync(message, username, password, operationId, primarySettings, "primary");
            return;
        }
        catch (Exception ex) when (IsSmtpConnectTransient(ex) && ShouldTryGmail465Fallback(primarySettings))
        {
            _logger.LogWarning(ex,
                "[EMAIL:{OperationId}] Primary SMTP connection failed. Trying Gmail fallback on 465/SSL.",
                operationId);

            var fallbackSettings = new SmtpSettings
            {
                Host = primarySettings.Host,
                Port = 465,
                SecureSocketOptions = SecureSocketOptions.SslOnConnect,
                TimeoutMs = primarySettings.TimeoutMs
            };

            await SendEmailViaSmtpWithSettingsAsync(message, username, password, operationId, fallbackSettings, "gmail-465-fallback");
        }
    }

    private async Task SendEmailViaSendGridAsync(MimeMessage message, string operationId, string apiKey)
    {
        var from = message.From.Mailboxes.FirstOrDefault();
        var recipients = message.To.Mailboxes.ToList();

        if (from == null)
        {
            throw new InvalidOperationException("Message 'From' is required for SendGrid transport.");
        }

        if (recipients.Count == 0)
        {
            throw new InvalidOperationException("Message must contain at least one recipient for SendGrid transport.");
        }

        var textBody = message.TextBody;
        var htmlBody = message.HtmlBody;

        if (string.IsNullOrWhiteSpace(textBody) && string.IsNullOrWhiteSpace(htmlBody))
        {
            throw new InvalidOperationException("Message body is empty.");
        }

        var contentList = new List<object>();
        if (!string.IsNullOrWhiteSpace(textBody))
        {
            contentList.Add(new { type = "text/plain", value = textBody });
        }

        if (!string.IsNullOrWhiteSpace(htmlBody))
        {
            contentList.Add(new { type = "text/html", value = htmlBody });
        }

        var payload = new
        {
            personalizations = new[]
            {
                new
                {
                    to = recipients.Select(r => new { email = r.Address, name = string.IsNullOrWhiteSpace(r.Name) ? null : r.Name }).ToArray(),
                    subject = message.Subject
                }
            },
            from = new
            {
                email = from.Address,
                name = string.IsNullOrWhiteSpace(from.Name) ? "SmartWork" : from.Name
            },
            content = contentList
        };

        var body = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        _logger.LogInformation(
            "[EMAIL:{OperationId}] Sending via SendGrid API. from={From} to={To} subject='{Subject}'",
            operationId,
            MaskEmail(from.Address),
            string.Join(",", recipients.Select(r => MaskEmail(r.Address))),
            message.Subject);

        var sw = Stopwatch.StartNew();
        using var response = await SendGridHttpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        sw.Stop();

        if ((int)response.StatusCode != 202)
        {
            _logger.LogError(
                "[EMAIL:{OperationId}] SendGrid API failed in {ElapsedMs}ms. status={StatusCode} body={Body}",
                operationId,
                sw.ElapsedMilliseconds,
                (int)response.StatusCode,
                TruncateForLog(responseBody));

            throw new InvalidOperationException($"SendGrid API failed with status {(int)response.StatusCode}: {TruncateForLog(responseBody, 800)}");
        }

        _logger.LogInformation("[EMAIL:{OperationId}] SendGrid API accepted message in {ElapsedMs}ms.", operationId, sw.ElapsedMilliseconds);
    }

    private async Task SendEmailThroughConfiguredTransportAsync(MimeMessage message, string operationId)
    {
        var mode = GetTransportMode();
        var (sendGridApiKey, sendGridSource) = GetSendGridApiKey();
        var hasSendGrid = !string.IsNullOrWhiteSpace(sendGridApiKey);

        _logger.LogInformation(
            "[EMAIL:{OperationId}] Transport selection. mode={Mode} hasSendGridKey={HasSendGrid} sendGridKeySource={SendGridKeySource}",
            operationId,
            mode,
            hasSendGrid,
            sendGridSource);

        if (mode == "sendgrid")
        {
            if (!hasSendGrid)
            {
                throw new InvalidOperationException("EMAIL_TRANSPORT=sendgrid but SENDGRID_API_KEY is not configured.");
            }

            await SendEmailViaSendGridAsync(message, operationId, sendGridApiKey!);
            return;
        }

        if (mode == "auto" && hasSendGrid)
        {
            try
            {
                await SendEmailViaSendGridAsync(message, operationId, sendGridApiKey!);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EMAIL:{OperationId}] SendGrid transport failed in auto mode, falling back to SMTP.", operationId);
            }
        }

        var (smtpUsername, smtpPassword) = GetCredentials();
        _logger.LogInformation("[EMAIL:{OperationId}] SMTP credentials loaded successfully for user {SmtpUser}", operationId, MaskEmail(smtpUsername));
        await SendEmailViaSmtpAsync(message, smtpUsername, smtpPassword, operationId);
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

            var fromAddress = ResolveFromAddress();

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("SmartWork", fromAddress));
            message.To.Add(new MailboxAddress("Admin", toEmail));
            message.Subject = "Nuova Richiesta di Smart Working";
            message.Body = new BodyBuilder
            {
                TextBody = $@"Una nuova richiesta di smart working è stata creata.

Dipendente: {employeeName}
Data: {dateString}

Accedi al sistema per revisione e approvazione."
            }.ToMessageBody();

            await SendEmailThroughConfiguredTransportAsync(message, operationId);
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

            var fromAddress = ResolveFromAddress();

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("SmartWork", fromAddress));
            message.To.Add(new MailboxAddress(employeeName, toEmail));
            message.Subject = approved ? "Richiesta approvata" : "Richiesta rifiutata";
            message.Body = new BodyBuilder
            {
                TextBody = approved
                    ? $"La tua richiesta di smart working per il {dateString} è stata approvata da {decidedBy}."
                    : $"La tua richiesta di smart working per il {dateString} è stata rifiutata da {decidedBy}."
            }.ToMessageBody();

            await SendEmailThroughConfiguredTransportAsync(message, operationId);
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

            var fromAddress = ResolveFromAddress();

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("SmartWork", fromAddress));
            message.To.Add(new MailboxAddress(username, toEmail));
            message.Subject = "Password temporanea SmartWork";
            message.Body = new BodyBuilder
            {
                TextBody = $@"Hai richiesto il ripristino password.

Nome utente: {username}
Password temporanea: {tempPassword}

Effettua l'accesso e cambia la password al più presto."
            }.ToMessageBody();

            await SendEmailThroughConfiguredTransportAsync(message, operationId);

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