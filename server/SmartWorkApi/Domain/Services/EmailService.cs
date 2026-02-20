using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Globalization;

public class EmailService
{
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

    private static async Task SendEmailAsync(MimeMessage message, string username, string password)
    {
        var settings = GetSmtpSettings();

        using var client = new SmtpClient
        {
            Timeout = settings.TimeoutMs
        };

        await client.ConnectAsync(settings.Host, settings.Port, settings.SecureSocketOptions);
        await client.AuthenticateAsync(username, password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    public async Task SendRequestNotificationAsync(string toEmail, string employeeName, string dateString)
    {
        try
        {
            var (username, password) = GetCredentials();

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

            await SendEmailAsync(message, username, password);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EMAIL ERROR] Failed to send notification: {ex.Message}; inner={ex.InnerException?.Message}");
        }
    }

    public async Task SendDecisionNotificationAsync(string toEmail, string employeeName, string dateString, bool approved, string decidedBy)
    {
        try
        {
            var (username, password) = GetCredentials();

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

            await SendEmailAsync(message, username, password);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EMAIL ERROR] Failed to send decision notification: {ex.Message}; inner={ex.InnerException?.Message}");
        }
    }

    public async Task<bool> SendTemporaryPasswordAsync(string toEmail, string username, string tempPassword)
    {
        try
        {
            var (smtpUsername, smtpPassword) = GetCredentials();

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

            await SendEmailAsync(message, smtpUsername, smtpPassword);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EMAIL ERROR] Failed to send temporary password: {ex.Message}; inner={ex.InnerException?.Message}");
            return false;
        }
    }
}