using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

public class EmailService
{
    private static (string Username, string Password) GetCredentials()
    {
        var username = Environment.GetEnvironmentVariable("SMTP_USERNAME")
            ?? throw new InvalidOperationException("SMTP_USERNAME environment variable is required");

        var password = Environment.GetEnvironmentVariable("SMTP_PASSWORD")
            ?? throw new InvalidOperationException("SMTP_PASSWORD environment variable is required");

        return (username, password);
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

            using var client = new SmtpClient();
            await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EMAIL ERROR] Failed to send notification: {ex.Message}");
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

            using var client = new SmtpClient();
            await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EMAIL ERROR] Failed to send decision notification: {ex.Message}");
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

            using var client = new SmtpClient();
            await client.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(smtpUsername, smtpPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EMAIL ERROR] Failed to send temporary password: {ex.Message}");
            return false;
        }
    }
}