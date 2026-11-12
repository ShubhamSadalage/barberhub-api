using System.ComponentModel.DataAnnotations;

namespace BarberHub.Api.Features.Auth.Dtos;

// ---------- Registration ----------

public class RegisterRequestDto
{
    [Required, StringLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    [Required, StringLength(100, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;

    public string? City { get; set; }

    public bool IsBarber { get; set; } = false;

    // Barber-only fields
    public string? ShopName { get; set; }
    public string? ShopDescription { get; set; }
    public string? Address { get; set; }
}

/// <summary>
/// Returned after Step 1 of registration. The ticket is an encrypted, tamper-proof
/// blob carrying the registration form so the stateless API doesn't need server-side
/// session state between "issue OTP" and "verify OTP + create account".
/// </summary>
public class RegisterInitiatedResponseDto
{
    public string RegistrationTicket { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    /// <summary>Only populated when the SMS provider is "Console" (local/dev testing).</summary>
    public string? DebugSmsCode { get; set; }
    /// <summary>Only populated when no SMTP server is configured (local/dev testing).</summary>
    public string? DebugEmailCode { get; set; }
}

public class RegisterVerifyRequestDto
{
    [Required] public string RegistrationTicket { get; set; } = string.Empty;

    [Required, StringLength(8, MinimumLength = 4)]
    public string EmailCode { get; set; } = string.Empty;

    public string? MobileCode { get; set; }
}

// ---------- Login ----------

public class LoginRequestDto
{
    [Required]
    public string EmailOrPhone { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    /// <summary>e.g. "Pixel 8 / Android 14" — stored alongside the refresh token for session management.</summary>
    public string? DeviceInfo { get; set; }
}

/// <summary>
/// Returned by /auth/login. Either tokens are populated (normal login),
/// or Requires2fa is true and the client must call /auth/2fa/challenge next.
/// </summary>
public class LoginResponseDto
{
    public bool Requires2fa { get; set; }
    public string? TwoFactorToken { get; set; }
    public bool RequiresTwoFactorEnrollment { get; set; }

    public TokenResponseDto? Tokens { get; set; }
}

public class TwoFactorChallengeRequestDto
{
    [Required] public string TwoFactorToken { get; set; } = string.Empty;
    [Required, StringLength(8, MinimumLength = 6)] public string Code { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
}

public class TwoFactorEnrollResponseDto
{
    public string TwoFactorToken { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string OtpAuthUri { get; set; } = string.Empty;
    public string QrCodePngDataUri { get; set; } = string.Empty;
}

public class TwoFactorEnrollConfirmRequestDto
{
    [Required] public string TwoFactorToken { get; set; } = string.Empty;
    [Required, StringLength(8, MinimumLength = 6)] public string Code { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
}

// ---------- Tokens ----------

public class TokenResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }
    public UserDto User { get; set; } = null!;
}

public class RefreshRequestDto
{
    [Required] public string RefreshToken { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
}

public class LogoutRequestDto
{
    [Required] public string RefreshToken { get; set; } = string.Empty;
}

// ---------- OTP passwordless login ----------

public class OtpRequestDto
{
    [Required, Display(Name = "Email or phone")]
    public string Recipient { get; set; } = string.Empty;
}

public class OtpVerifyRequestDto
{
    [Required] public string Recipient { get; set; } = string.Empty;
    [Required] public string Channel { get; set; } = "email"; // "email" | "sms"
    [Required, StringLength(8, MinimumLength = 4)]
    public string Code { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
}

// ---------- Google Sign-In (mobile) ----------

public class GoogleLoginRequestDto
{
    /// <summary>ID token obtained from @react-native-google-signin/google-signin on the device.</summary>
    [Required] public string IdToken { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
}

// ---------- Password management ----------

public class ForgotPasswordRequestDto
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequestDto
{
    [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    [Required] public string Token { get; set; } = string.Empty;
    [Required, StringLength(100, MinimumLength = 6)] public string NewPassword { get; set; } = string.Empty;
}

public class ChangePasswordRequestDto
{
    [Required, DataType(DataType.Password)] public string CurrentPassword { get; set; } = string.Empty;
    [Required, StringLength(100, MinimumLength = 6)] public string NewPassword { get; set; } = string.Empty;
}

// ---------- Profile ----------

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string Role { get; set; } = string.Empty;    // "User" | "Admin" (barber) | "SuperAdmin"
    public string? ProfileImageUrl { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Barber fields
    public string? ShopName { get; set; }
    public string? ShopDescription { get; set; }
    public string? ApprovalStatus { get; set; }   // NotApplicable | Pending | Approved | Rejected
    public string? ApprovalRejectionReason { get; set; }
    public DayOfWeek? WeeklyHoliday { get; set; }
    public TimeOnly? OpeningTime { get; set; }
    public TimeOnly? ClosingTime { get; set; }

    public bool TwoFactorEnabled { get; set; }
}

public class UpdateProfileRequestDto
{
    [Required, StringLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Phone] public string? PhoneNumber { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Barber fields (ignored server-side for non-barbers)
    public string? ShopName { get; set; }
    public string? ShopDescription { get; set; }
    public DayOfWeek? WeeklyHoliday { get; set; }
    public TimeOnly? OpeningTime { get; set; }
    public TimeOnly? ClosingTime { get; set; }
}
