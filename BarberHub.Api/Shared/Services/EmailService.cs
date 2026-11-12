using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BarberHub.Api.Shared.Services;

public interface IEmailService
{
    /// <summary>Sends an email. Returns true on success; false on configuration / delivery error.</summary>
    Task<bool> SendAsync(string toEmail, string subject, string htmlBody);

    /// <summary>Tests SMTP delivery and returns diagnostic details or exact error message.</summary>
    Task<(bool Success, string Message)> TestConnectionAsync(string toEmail);
}

public class EmailSettings
{
    public string SmtpServer { get; set; } = string.Empty;
    public int Port { get; set; }
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool EnableSsl { get; set; }
}

public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IOptions<EmailSettings> options, ILogger<EmailService> logger)
    {
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string toEmail, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(_settings.SmtpServer))
        {
            // Local-dev fallback (no SMTP configured) — log instead of sending, like the SMS Console provider.
            _logger.LogWarning("Email [Console] to {Email} — {Subject}\n{Body}", toEmail, subject, htmlBody);
            return true;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_settings.SmtpServer, _settings.Port,
                _settings.EnableSsl ? MailKit.Security.SecureSocketOptions.StartTls : MailKit.Security.SecureSocketOptions.None);
            await client.AuthenticateAsync(_settings.Username, _settings.Password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent to {Email}", toEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
            return false;
        }
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(string toEmail)
    {
        if (string.IsNullOrWhiteSpace(_settings.SmtpServer))
        {
            return (false, "SmtpServer is empty in appsettings.json. Emails currently fall back to console logging.");
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = "Barber Hub - SMTP Test";

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = "<h3>Barber Hub SMTP Test</h3><p>Your SMTP configuration is working perfectly!</p>"
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(_settings.SmtpServer, _settings.Port,
                _settings.EnableSsl ? MailKit.Security.SecureSocketOptions.StartTls : MailKit.Security.SecureSocketOptions.None);
            await client.AuthenticateAsync(_settings.Username, _settings.Password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            return (true, $"Test email sent successfully to {toEmail} using {_settings.SmtpServer}:{_settings.Port}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP test failed for {Email}", toEmail);
            return (false, ex.Message);
        }
    }
}
