using System.Security.Cryptography;
using System.Text;
using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Infrastructure.Persistence;
using BarberHub.Api.Shared.Config;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BarberHub.Api.Shared.Services;

public enum OtpChannel { Email, Sms }

public class OtpIssueResult
{
    public bool Sent { get; set; }
    public string? Error { get; set; }
    public DateTime? ExpiresAt { get; set; }
    /// <summary>Test/dev only — the plaintext code, when running with the Console SMS provider so you can copy it.</summary>
    public string? DebugCode { get; set; }
}

public class OtpVerifyResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    /// <summary>UserId of the matching user (if any). Null if no account exists for this recipient.</summary>
    public string? UserId { get; set; }
}

public interface IOtpService
{
    Task<OtpIssueResult> IssueAsync(string recipient, OtpChannel channel, OtpPurpose purpose, string? ipAddress = null);
    Task<OtpVerifyResult> VerifyAsync(string recipient, string code, OtpPurpose purpose, OtpChannel channel);
}

public class OtpService : IOtpService
{
    private readonly ApplicationDbContext _db;
    private readonly OtpOptions _otp;
    private readonly RateLimitOptions _rate;
    private readonly SmsOptions _smsOpts;
    private readonly EmailSettings _emailOpts;
    private readonly IEmailService _email;
    private readonly ISmsService _sms;
    private readonly ILogger<OtpService> _logger;

    public OtpService(
        ApplicationDbContext db,
        IOptions<OtpOptions> otp,
        IOptions<RateLimitOptions> rate,
        IOptions<SmsOptions> smsOpts,
        IOptions<EmailSettings> emailOpts,
        IEmailService email,
        ISmsService sms,
        ILogger<OtpService> logger)
    {
        _db = db;
        _otp = otp.Value;
        _rate = rate.Value;
        _smsOpts = smsOpts.Value;
        _emailOpts = emailOpts.Value;
        _email = email;
        _sms = sms;
        _logger = logger;
    }

    public async Task<OtpIssueResult> IssueAsync(string recipient, OtpChannel channel, OtpPurpose purpose, string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(recipient))
            return new OtpIssueResult { Sent = false, Error = "Recipient is required." };

        recipient = recipient.Trim();
        var normalized = channel == OtpChannel.Email ? recipient.ToLowerInvariant() : recipient;

        // Rate limit: max N OTPs per hour for this recipient
        var oneHourAgo = DateTime.UtcNow.AddHours(-1);
        var recentCount = await _db.OtpCodes
            .CountAsync(o => o.Recipient == normalized && o.Purpose == purpose && o.CreatedAt >= oneHourAgo);
        if (recentCount >= _rate.OtpPerHour)
            return new OtpIssueResult { Sent = false, Error = "Too many requests. Please try again in an hour." };

        // Resend cooldown — block if last code created within the cooldown window
        var cooldownAgo = DateTime.UtcNow.AddSeconds(-_otp.ResendCooldownSeconds);
        var recent = await _db.OtpCodes
            .Where(o => o.Recipient == normalized && o.Purpose == purpose && o.CreatedAt >= cooldownAgo)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();
        if (recent is not null)
        {
            var wait = (int)(_otp.ResendCooldownSeconds - (DateTime.UtcNow - recent.CreatedAt).TotalSeconds);
            return new OtpIssueResult { Sent = false, Error = $"Please wait {Math.Max(1, wait)}s before requesting another code." };
        }

        // Invalidate any unused/unexpired codes for the same recipient+purpose
        var stale = await _db.OtpCodes
            .Where(o => o.Recipient == normalized && o.Purpose == purpose && o.ConsumedAt == null && o.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();
        foreach (var s in stale) s.ConsumedAt = DateTime.UtcNow;

        // Generate code
        var code = GenerateNumericCode(_otp.CodeLength);
        var hash = HashCode(code, normalized);
        var expiresAt = DateTime.UtcNow.AddMinutes(_otp.ExpiryMinutes);

        var entity = new OtpCode
        {
            Recipient = normalized,
            CodeHash = hash,
            Purpose = purpose,
            ExpiresAt = expiresAt,
            IpAddress = ipAddress
        };
        await _db.OtpCodes.AddAsync(entity);

        // Try to attach a UserId if the account exists (helps for audit; not required)
        var lookup = channel == OtpChannel.Email
            ? await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedEmail == normalized.ToUpperInvariant())
            : await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.PhoneNumber == normalized);
        if (lookup is not null) entity.UserId = lookup.Id;

        await _db.SaveChangesAsync();

        // Deliver
        bool delivered;
        if (channel == OtpChannel.Email)
        {
            var (subject, html) = BuildEmailTemplate(code, purpose, _otp.ExpiryMinutes);
            delivered = await _email.SendAsync(normalized, subject, html);
        }
        else
        {
            delivered = await _sms.SendAsync(normalized, $"Your Barber Hub code is {code}. Valid {_otp.ExpiryMinutes} min.");
        }

        return new OtpIssueResult
        {
            Sent = delivered,
            Error = delivered ? null : "Could not deliver code. Please try again.",
            ExpiresAt = expiresAt,
            // Surface the code only in local-dev fallback modes (SMS "Console" provider, or no SMTP configured) — for local testing
            DebugCode = (channel == OtpChannel.Sms && _smsOpts.Provider?.Equals("Console", StringComparison.OrdinalIgnoreCase) == true)
                        || (channel == OtpChannel.Email && string.IsNullOrWhiteSpace(_emailOpts.SmtpServer))
                        ? code : null
        };
    }

    public async Task<OtpVerifyResult> VerifyAsync(string recipient, string code, OtpPurpose purpose, OtpChannel channel)
    {
        if (string.IsNullOrWhiteSpace(recipient) || string.IsNullOrWhiteSpace(code))
            return new OtpVerifyResult { Ok = false, Error = "Code is required." };

        var normalized = channel == OtpChannel.Email ? recipient.Trim().ToLowerInvariant() : recipient.Trim();

        var entity = await _db.OtpCodes
            .Where(o => o.Recipient == normalized && o.Purpose == purpose && o.ConsumedAt == null)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (entity is null)
            return new OtpVerifyResult { Ok = false, Error = "No active code found. Please request a new one." };

        if (entity.ExpiresAt < DateTime.UtcNow)
            return new OtpVerifyResult { Ok = false, Error = "Code expired. Please request a new one." };

        if (entity.AttemptCount >= _otp.MaxAttempts)
        {
            entity.ConsumedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return new OtpVerifyResult { Ok = false, Error = "Too many attempts. Please request a new code." };
        }

        var hash = HashCode(code.Trim(), normalized);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(hash),
                Encoding.ASCII.GetBytes(entity.CodeHash)))
        {
            entity.AttemptCount++;
            await _db.SaveChangesAsync();
            return new OtpVerifyResult { Ok = false, Error = "Incorrect code." };
        }

        entity.ConsumedAt = DateTime.UtcNow;

        // Resolve user
        var user = channel == OtpChannel.Email
            ? await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedEmail == normalized.ToUpperInvariant())
            : await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.PhoneNumber == normalized);

        await _db.SaveChangesAsync();

        return new OtpVerifyResult { Ok = true, UserId = user?.Id };
    }

    private static string GenerateNumericCode(int length)
    {
        // Cryptographically random numeric code
        Span<byte> buf = stackalloc byte[length];
        RandomNumberGenerator.Fill(buf);
        var sb = new StringBuilder(length);
        foreach (var b in buf) sb.Append((b % 10).ToString());
        return sb.ToString();
    }

    private static string HashCode(string code, string recipient)
    {
        // Salt the hash with the recipient so leaked hashes can't be cross-reused.
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes($"{recipient}:{code}"));
        return Convert.ToHexString(bytes);
    }

    private static (string Subject, string Html) BuildEmailTemplate(string code, OtpPurpose purpose, int expiryMinutes)
    {
        string subject;
        string title;
        string intro;

        switch (purpose)
        {
            case OtpPurpose.PasswordReset:
                subject = "Reset Your Password - Barber Hub";
                title = "Reset Your Password";
                intro = "We received a request to reset your password. Use the verification code below to set a new password in the Barber Hub app:";
                break;
            case OtpPurpose.SuperAdmin2Fa:
                subject = "SuperAdmin Security Code - Barber Hub";
                title = "Security Verification";
                intro = "A login attempt requires two-factor verification. Enter the code below to complete sign-in:";
                break;
            case OtpPurpose.Login:
            default:
                subject = "Your Barber Hub Verification Code";
                title = "Verify Your Email";
                intro = "Use the verification code below to verify your email address and continue with Barber Hub:";
                break;
        }

        var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>{subject}</title>
</head>
<body style=""margin: 0; padding: 0; background-color: #f1f5f9; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; color: #1e293b;"">
  <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" style=""background-color: #f1f5f9; padding: 32px 16px;"">
    <tr>
      <td align=""center"">
        <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" style=""max-width: 520px; background-color: #ffffff; border-radius: 16px; border: 1px solid #e2e8f0; overflow: hidden; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.05);"">
          <!-- Header -->
          <tr>
            <td style=""background-color: #0f172a; padding: 28px 24px; text-align: center;"">
              <div style=""display: inline-block; background-color: #ef5b5b; color: #ffffff; font-size: 20px; font-weight: 800; padding: 6px 14px; border-radius: 8px; letter-spacing: 1px;"">
                BARBER HUB
              </div>
              <p style=""margin: 8px 0 0 0; color: #94a3b8; font-size: 13px; font-weight: 500;"">
                Premium Grooming & Appointment Booking
              </p>
            </td>
          </tr>
          <!-- Body Content -->
          <tr>
            <td style=""padding: 32px 28px;"">
              <h2 style=""margin: 0 0 12px 0; font-size: 22px; font-weight: 700; color: #0f172a;"">
                {title}
              </h2>
              <p style=""margin: 0 0 24px 0; font-size: 15px; line-height: 1.6; color: #475569;"">
                {intro}
              </p>
              
              <!-- Verification Code Box -->
              <div style=""background-color: #f8fafc; border: 2px dashed #cbd5e1; border-radius: 12px; padding: 20px; text-align: center; margin: 24px 0;"">
                <div style=""font-size: 12px; font-weight: 600; text-transform: uppercase; letter-spacing: 1.5px; color: #64748b; margin-bottom: 8px;"">
                  Your Verification Code
                </div>
                <div style=""font-family: 'Courier New', Courier, monospace; font-size: 34px; font-weight: 800; letter-spacing: 8px; color: #ef5b5b;"">
                  {code}
                </div>
              </div>

              <!-- Notes -->
              <p style=""margin: 0 0 8px 0; font-size: 13px; color: #64748b; line-height: 1.5;"">
                Expires in <strong>{expiryMinutes} minutes</strong>. Do not share this code with anyone.
              </p>
              <p style=""margin: 0; font-size: 13px; color: #94a3b8; line-height: 1.5;"">
                If you did not request this, you can safely ignore this email.
              </p>
            </td>
          </tr>
          <!-- Footer -->
          <tr>
            <td style=""background-color: #f8fafc; border-top: 1px solid #e2e8f0; padding: 20px 24px; text-align: center;"">
              <p style=""margin: 0; font-size: 12px; color: #94a3b8;"">
                &copy; {DateTime.UtcNow.Year} Barber Hub. All rights reserved.
              </p>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>";

        return (subject, html);
    }
}
