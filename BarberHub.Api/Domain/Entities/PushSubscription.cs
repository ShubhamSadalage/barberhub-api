namespace BarberHub.Api.Domain.Entities;

/// <summary>
/// FCM device token for a mobile install. One user can have multiple devices
/// (e.g. phone + tablet, or reinstall). Replaces the old browser Web Push
/// subscription model used by the MVC web app.
/// </summary>
public class PushSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    /// <summary>FCM registration token from @react-native-firebase/messaging's getToken().</summary>
    public string DeviceToken { get; set; } = string.Empty;

    public string Platform { get; set; } = "android";   // "android" | "ios" (future)
    public string? DeviceModel { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
