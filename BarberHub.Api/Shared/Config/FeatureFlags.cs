namespace BarberHub.Api.Shared.Config;

/// <summary>
/// Bound from the "Features" section of appsettings.json.
/// Lets us flip features on/off without code changes.
/// </summary>
public class FeatureFlags
{
    public bool OnlinePaymentEnabled { get; set; } = true;
    public bool ProductsEnabled { get; set; } = true;
    public bool EarningsFinancialsEnabled { get; set; } = true;
    public bool GoogleLoginEnabled { get; set; } = false;
    public bool EmailOtpLoginEnabled { get; set; } = true;
    public bool SmsOtpLoginEnabled { get; set; } = false;
    public bool MapsEnabled { get; set; } = false;
    public bool PushEnabled { get; set; } = false;   // FCM push (was WebPushEnabled in the web app)
    public bool SuperAdminTwoFactorEnabled { get; set; } = true;
}

public class FirebaseOptions
{
    /// <summary>Full JSON contents of the Firebase service-account key (paste into user-secrets/env for prod).</summary>
    public string? ServiceAccountJson { get; set; }
    /// <summary>Alternatively, a path to the service-account JSON file on disk.</summary>
    public string? ServiceAccountJsonPath { get; set; }
}

public class OtpOptions
{
    public int CodeLength { get; set; } = 6;
    public int ExpiryMinutes { get; set; } = 5;
    public int MaxAttempts { get; set; } = 5;
    public int ResendCooldownSeconds { get; set; } = 60;
}

public class SmsOptions
{
    public string Provider { get; set; } = "Console";  // "Console" | "MSG91" | "Twilio" | "Fast2SMS"
    public string ApiKey { get; set; } = string.Empty;
    public string SenderId { get; set; } = "BHUB";
    public string TemplateId { get; set; } = string.Empty;
}

public class GoogleOptions
{
    public GoogleMapsOptions Maps { get; set; } = new();
    public GoogleOAuthOptions OAuth { get; set; } = new();
}

public class GoogleMapsOptions
{
    public string ApiKey { get; set; } = string.Empty;
}

public class GoogleOAuthOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

public class QueueOptions
{
    public int TickIntervalSeconds { get; set; } = 60;
    public int AutoCancelPendingMinutes { get; set; } = 30;
    public int ReminderMinutesBefore { get; set; } = 60;
}

public class RateLimitOptions
{
    public int OtpPerHour { get; set; } = 6;
    public int LoginPerMinute { get; set; } = 10;
}
