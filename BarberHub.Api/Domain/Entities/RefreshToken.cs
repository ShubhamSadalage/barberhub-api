namespace BarberHub.Api.Domain.Entities;

/// <summary>
/// Opaque refresh token issued alongside a short-lived JWT access token.
/// We store only the SHA-256 hash, never the plaintext token.
/// One row per device/session so a user can be logged in on multiple devices,
/// and a single device can be revoked (e.g. "log out this device") without
/// affecting others.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }

    /// <summary>When a token is refreshed, the old row is revoked and points at the new one (rotation chain) so reuse of a stolen token can be detected.</summary>
    public Guid? ReplacedByTokenId { get; set; }

    public string? DeviceInfo { get; set; }   // e.g. "Android 14 / Pixel 8" from RN DeviceInfo
    public string? IpAddress { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;
}
