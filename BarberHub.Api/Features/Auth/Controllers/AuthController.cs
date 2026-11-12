using BarberHub.Api.Domain.Constants;
using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Features.Auth.Dtos;
using BarberHub.Api.Features.Auth.Services;
using BarberHub.Api.Features.Notifications;
using BarberHub.Api.Shared.Services;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace BarberHub.Api.Features.Auth.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IJwtTokenService _jwt;
    private readonly IOtpService _otp;
    private readonly ITwoFactorService _twoFactor;
    private readonly INotificationService _notifications;
    private readonly ICurrentUserService _currentUser;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly Shared.Config.GoogleOptions _googleOptions;
    private readonly Shared.Config.FeatureFlags _features;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        IJwtTokenService jwt,
        IOtpService otp,
        ITwoFactorService twoFactor,
        INotificationService notifications,
        ICurrentUserService currentUser,
        UserManager<ApplicationUser> userManager,
        IOptions<Shared.Config.GoogleOptions> googleOptions,
        IOptions<Shared.Config.FeatureFlags> features,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _jwt = jwt;
        _otp = otp;
        _twoFactor = twoFactor;
        _notifications = notifications;
        _currentUser = currentUser;
        _userManager = userManager;
        _googleOptions = googleOptions.Value;
        _features = features.Value;
        _logger = logger;
    }

    // ============== Registration (2-step OTP) ==============

    [HttpPost("register")]
    [EnableRateLimiting("otp")]
    public async Task<IActionResult> Register(RegisterRequestDto dto)
    {
        dto.Email = dto.Email.Trim().ToLowerInvariant();
        dto.PhoneNumber = string.IsNullOrWhiteSpace(dto.PhoneNumber) ? null : dto.PhoneNumber.Trim();

        var pre = await _authService.ValidateRegistrationAsync(dto);
        if (!pre.IsSuccess) return BadRequest(new { errors = pre.Errors });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var emailOtp = await _otp.IssueAsync(dto.Email, OtpChannel.Email, OtpPurpose.Login, ip);
        if (!emailOtp.Sent)
            return BadRequest(new { errors = new[] { $"Email verification code: {emailOtp.Error ?? "could not be sent."}" } });

        var ticket = _authService.CreateRegistrationTicket(dto);

        return Ok(new RegisterInitiatedResponseDto
        {
            RegistrationTicket = ticket,
            Message = $"We sent a verification code to {dto.Email}.",
            DebugEmailCode = emailOtp.DebugCode
        });
    }

    [HttpPost("register/verify")]
    public async Task<IActionResult> RegisterVerify(RegisterVerifyRequestDto v)
    {
        var dto = _authService.ReadRegistrationTicket(v.RegistrationTicket);
        if (dto is null)
            return BadRequest(new { errors = new[] { "Your registration session has expired. Please start again." } });

        var emailCheck = await _otp.VerifyAsync(dto.Email, v.EmailCode, OtpPurpose.Login, OtpChannel.Email);
        if (!emailCheck.Ok)
            return BadRequest(new { errors = new[] { emailCheck.Error ?? "Email verification code is invalid." } });

        var create = await _authService.CompleteRegistrationAsync(dto);
        if (!create.IsSuccess) return BadRequest(new { errors = create.Errors });

        var user = create.Value!;

        if (dto.IsBarber)
        {
            await _notifications.NotifyAllSuperAdminsAsync(
                NotificationType.BarberRegistration,
                "New barber awaiting approval",
                $"{dto.FullName} ({dto.ShopName}) has registered and is waiting for approval.",
                "SuperAdmin/Approvals");
        }

        var roles = await _userManager.GetRolesAsync(user);
        var pair = await _jwt.IssueTokenPairAsync(user, roles, ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());
        var userDto = await _authService.ToUserDtoAsync(user);

        return Ok(new TokenResponseDto
        {
            AccessToken = pair.AccessToken,
            RefreshToken = pair.RefreshToken,
            AccessTokenExpiresAt = pair.AccessTokenExpiresAt,
            User = userDto
        });
    }

    // ============== Login (password) ==============

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequestDto dto)
    {
        var result = await _authService.ValidateLoginAsync(dto);
        if (!result.IsSuccess) return Unauthorized(new { error = result.Error });

        var user = result.Value!;
        var roles = await _userManager.GetRolesAsync(user);

        if (_twoFactor.IsRequiredFor(user, roles))
        {
            var challengeToken = _jwt.IssueTwoFactorChallengeToken(user.Id);
            var enrolled = await _userManager.GetTwoFactorEnabledAsync(user);
            return Ok(new LoginResponseDto
            {
                Requires2fa = true,
                TwoFactorToken = challengeToken,
                RequiresTwoFactorEnrollment = !enrolled
            });
        }

        var pair = await _jwt.IssueTokenPairAsync(user, roles, dto.DeviceInfo, HttpContext.Connection.RemoteIpAddress?.ToString());
        var userDto = await _authService.ToUserDtoAsync(user);

        return Ok(new LoginResponseDto
        {
            Requires2fa = false,
            Tokens = new TokenResponseDto
            {
                AccessToken = pair.AccessToken,
                RefreshToken = pair.RefreshToken,
                AccessTokenExpiresAt = pair.AccessTokenExpiresAt,
                User = userDto
            }
        });
    }

    // ============== Two-Factor (SuperAdmin) ==============

    [HttpPost("2fa/enroll")]
    public async Task<IActionResult> TwoFactorEnroll([FromBody] string twoFactorToken)
    {
        var userId = _jwt.ValidateTwoFactorChallengeToken(twoFactorToken);
        if (userId is null) return Unauthorized();
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return Unauthorized();

        var setup = await _twoFactor.EnrollAsync(user, "Barber Hub");
        return Ok(new TwoFactorEnrollResponseDto
        {
            TwoFactorToken = twoFactorToken,
            Secret = setup.Secret,
            OtpAuthUri = setup.OtpAuthUri,
            QrCodePngDataUri = setup.QrCodePngDataUri
        });
    }

    [HttpPost("2fa/enroll/confirm")]
    public async Task<IActionResult> TwoFactorEnrollConfirm(TwoFactorEnrollConfirmRequestDto dto)
    {
        var userId = _jwt.ValidateTwoFactorChallengeToken(dto.TwoFactorToken);
        if (userId is null) return Unauthorized();
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return Unauthorized();

        var secret = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(secret) || !_twoFactor.VerifyCode(secret, dto.Code))
            return BadRequest(new { error = "Code is invalid. Please try again." });

        await _twoFactor.ConfirmEnabledAsync(user);

        var roles = await _userManager.GetRolesAsync(user);
        var pair = await _jwt.IssueTokenPairAsync(user, roles, dto.DeviceInfo, HttpContext.Connection.RemoteIpAddress?.ToString());
        var userDto = await _authService.ToUserDtoAsync(user);

        return Ok(new TokenResponseDto
        {
            AccessToken = pair.AccessToken,
            RefreshToken = pair.RefreshToken,
            AccessTokenExpiresAt = pair.AccessTokenExpiresAt,
            User = userDto
        });
    }

    [HttpPost("2fa/challenge")]
    public async Task<IActionResult> TwoFactorChallenge(TwoFactorChallengeRequestDto dto)
    {
        var userId = _jwt.ValidateTwoFactorChallengeToken(dto.TwoFactorToken);
        if (userId is null) return Unauthorized(new { error = "2FA session expired. Please login again." });
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return Unauthorized();

        var secret = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(secret) || !_twoFactor.VerifyCode(secret, dto.Code))
            return BadRequest(new { error = "Code is invalid." });

        var roles = await _userManager.GetRolesAsync(user);
        var pair = await _jwt.IssueTokenPairAsync(user, roles, dto.DeviceInfo, HttpContext.Connection.RemoteIpAddress?.ToString());
        var userDto = await _authService.ToUserDtoAsync(user);

        return Ok(new TokenResponseDto
        {
            AccessToken = pair.AccessToken,
            RefreshToken = pair.RefreshToken,
            AccessTokenExpiresAt = pair.AccessTokenExpiresAt,
            User = userDto
        });
    }

    // ============== Passwordless OTP login ==============

    [HttpPost("otp/request")]
    [EnableRateLimiting("otp")]
    public async Task<IActionResult> OtpRequest(OtpRequestDto dto)
    {
        var input = dto.Recipient.Trim().ToLowerInvariant();
        var isEmail = input.Contains('@');

        if (!isEmail)
            return BadRequest(new { error = "Phone number login is disabled. Please enter your email address." });

        if (!_features.EmailOtpLoginEnabled)
            return BadRequest(new { error = "Email OTP login is disabled." });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        // Do not reveal whether the account exists — always attempt to send.
        var result = await _otp.IssueAsync(input, OtpChannel.Email, OtpPurpose.Login, ip);
        if (!result.Sent) return BadRequest(new { error = result.Error ?? "Could not send code." });

        return Ok(new { message = "Code sent.", debugCode = result.DebugCode, channel = "email" });
    }

    [HttpPost("otp/verify")]
    public async Task<IActionResult> OtpVerify(OtpVerifyRequestDto dto)
    {
        var input = dto.Recipient.Trim().ToLowerInvariant();
        if (!input.Contains('@'))
            return BadRequest(new { error = "Phone number login is disabled. Please verify with your email address." });

        var v = await _otp.VerifyAsync(input, dto.Code, OtpPurpose.Login, OtpChannel.Email);
        if (!v.Ok) return BadRequest(new { error = v.Error ?? "Verification failed." });

        if (string.IsNullOrEmpty(v.UserId))
            return BadRequest(new { error = "No account found for this email. Please register first." });

        var user = await _userManager.FindByIdAsync(v.UserId);
        if (user is null || !user.IsActive || user.IsDeleted)
            return BadRequest(new { error = "Account is not active." });

        var roles = await _userManager.GetRolesAsync(user);

        if (_twoFactor.IsRequiredFor(user, roles))
        {
            var challengeToken = _jwt.IssueTwoFactorChallengeToken(user.Id);
            return Ok(new LoginResponseDto { Requires2fa = true, TwoFactorToken = challengeToken });
        }

        var pair = await _jwt.IssueTokenPairAsync(user, roles, dto.DeviceInfo, HttpContext.Connection.RemoteIpAddress?.ToString());
        var userDto = await _authService.ToUserDtoAsync(user);

        return Ok(new TokenResponseDto
        {
            AccessToken = pair.AccessToken,
            RefreshToken = pair.RefreshToken,
            AccessTokenExpiresAt = pair.AccessTokenExpiresAt,
            User = userDto
        });
    }

    // ============== Google Sign-In (mobile) ==============

    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin(GoogleLoginRequestDto dto)
    {
        if (!_features.GoogleLoginEnabled)
            return BadRequest(new { error = "Google sign-in is currently disabled." });

        GoogleJsonWebSignature.Payload payload;
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings();
            if (!string.IsNullOrWhiteSpace(_googleOptions.OAuth.ClientId))
            {
                settings.Audience = new[] { _googleOptions.OAuth.ClientId };
            }
            payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken, settings);
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Invalid Google ID token supplied");
            return BadRequest(new { error = "Invalid Google token." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate Google token");
            return BadRequest(new { error = "Failed to validate Google token." });
        }

        var subject = payload.Subject;
        var email = payload.Email?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "Google account did not provide an email address." });

        var name = payload.Name ?? email.Split('@')[0];

        var user = _userManager.Users.FirstOrDefault(u => u.GoogleSubjectId == subject);

        if (user is null)
        {
            user = await _userManager.FindByEmailAsync(email);
            if (user is not null && string.IsNullOrEmpty(user.GoogleSubjectId))
            {
                user.GoogleSubjectId = subject;
                user.EmailConfirmed = true;
                await _userManager.UpdateAsync(user);
            }
        }

        if (user is null)
        {
            // Auto-create a regular User account. Barbers must register via the
            // form so they can supply shop details for the approval workflow.
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = name,
                GoogleSubjectId = subject,
                IsActive = true,
                ApprovalStatus = BarberApprovalStatus.NotApplicable
            };

            var create = await _userManager.CreateAsync(user);
            if (!create.Succeeded)
                return BadRequest(new { errors = create.Errors.Select(e => e.Description) });
            await _userManager.AddToRoleAsync(user, AppRoles.User);
        }

        if (user.IsDeleted || !user.IsActive)
            return BadRequest(new { error = "Your account is no longer active. Please contact support." });

        var roles = await _userManager.GetRolesAsync(user);
        var pair = await _jwt.IssueTokenPairAsync(user, roles, dto.DeviceInfo, HttpContext.Connection.RemoteIpAddress?.ToString());
        var userDto = await _authService.ToUserDtoAsync(user);

        return Ok(new TokenResponseDto
        {
            AccessToken = pair.AccessToken,
            RefreshToken = pair.RefreshToken,
            AccessTokenExpiresAt = pair.AccessTokenExpiresAt,
            User = userDto
        });
    }

    // ============== Token refresh / logout ==============

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequestDto dto)
    {
        var pair = await _jwt.RefreshAsync(dto.RefreshToken, dto.DeviceInfo, HttpContext.Connection.RemoteIpAddress?.ToString());
        if (pair is null) return Unauthorized(new { error = "Invalid or expired refresh token. Please login again." });

        return Ok(new { pair.AccessToken, pair.RefreshToken, pair.AccessTokenExpiresAt });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(LogoutRequestDto dto)
    {
        await _jwt.RevokeAsync(dto.RefreshToken);
        return Ok(new { ok = true });
    }

    [HttpPost("logout-all-devices")]
    [Authorize]
    public async Task<IActionResult> LogoutAllDevices()
    {
        await _jwt.RevokeAllForUserAsync(_currentUser.UserId!);
        return Ok(new { ok = true });
    }

    // ============== Password management ==============

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequestDto dto)
    {
        await _authService.ForgotPasswordAsync(dto.Email);
        return Ok(new { message = "If this email exists, we have sent a reset code." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequestDto dto)
    {
        var result = await _authService.ResetPasswordAsync(dto);
        if (!result.IsSuccess) return BadRequest(new { errors = result.Errors });
        return Ok(new { message = "Password reset successful. Please login." });
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequestDto dto)
    {
        var result = await _authService.ChangePasswordAsync(_currentUser.UserId!, dto);
        if (!result.IsSuccess) return BadRequest(new { errors = result.Errors });

        // Changing the password invalidates all existing sessions for safety.
        await _jwt.RevokeAllForUserAsync(_currentUser.UserId!);
        return Ok(new { message = "Password changed. Please login again on all devices." });
    }

    // ============== Profile ==============

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await _authService.GetByIdAsync(_currentUser.UserId!);
        if (user is null) return NotFound();
        return Ok(await _authService.ToUserDtoAsync(user));
    }

    [HttpPut("me")]
    [Authorize]
    public async Task<IActionResult> UpdateMe(UpdateProfileRequestDto dto)
    {
        var result = await _authService.UpdateProfileAsync(_currentUser.UserId!, dto, imagePath: null);
        if (!result.IsSuccess) return BadRequest(new { errors = result.Errors });

        var user = await _authService.GetByIdAsync(_currentUser.UserId!);
        return Ok(await _authService.ToUserDtoAsync(user!));
    }

    /// <summary>Multipart upload for the profile picture, kept separate from the JSON profile update above.</summary>
    [HttpPost("me/photo")]
    [Authorize]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadPhoto(IFormFile file, [FromServices] IWebHostEnvironment env)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "No file uploaded." });

        var allowedExt = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExt.Contains(ext)) return BadRequest(new { error = "Unsupported image format." });

        var uploadsDir = Path.Combine(env.WebRootPath ?? "wwwroot", "uploads", "profiles");
        Directory.CreateDirectory(uploadsDir);
        var fileName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(uploadsDir, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
            await file.CopyToAsync(stream);

        var relativePath = $"/uploads/profiles/{fileName}";
        var user = await _authService.GetByIdAsync(_currentUser.UserId!);
        if (user is null) return NotFound();

        var dto = new UpdateProfileRequestDto
        {
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            City = user.City,
            Address = user.Address,
            Latitude = user.Latitude,
            Longitude = user.Longitude,
            ShopName = user.ShopName,
            ShopDescription = user.ShopDescription,
            WeeklyHoliday = user.WeeklyHoliday,
            OpeningTime = user.OpeningTime,
            ClosingTime = user.ClosingTime
        };
        await _authService.UpdateProfileAsync(_currentUser.UserId!, dto, relativePath);

        return Ok(new { profileImageUrl = relativePath });
    }
}
