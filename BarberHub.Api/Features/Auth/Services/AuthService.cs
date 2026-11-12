using System.Text.Json;
using BarberHub.Api.Domain.Constants;
using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Features.Auth.Dtos;
using BarberHub.Api.Shared;
using BarberHub.Api.Shared.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace BarberHub.Api.Features.Auth.Services;

public interface IAuthService
{
    /// <summary>Pre-flight uniqueness check. Does not create the account.</summary>
    Task<Result> ValidateRegistrationAsync(RegisterRequestDto dto);

    /// <summary>Encrypts the registration form into an opaque ticket string (valid ~15 min) carried by the client between step 1 (OTP issue) and step 2 (OTP verify).</summary>
    string CreateRegistrationTicket(RegisterRequestDto dto);

    /// <summary>Decrypts + validates the ticket's age. Returns null if tampered/expired.</summary>
    RegisterRequestDto? ReadRegistrationTicket(string ticket);

    Task<Result<ApplicationUser>> CompleteRegistrationAsync(RegisterRequestDto dto);
    Task<Result<ApplicationUser>> ValidateLoginAsync(LoginRequestDto dto);
    Task<Result> ForgotPasswordAsync(string email);
    Task<Result> ResetPasswordAsync(ResetPasswordRequestDto dto);
    Task<Result> ChangePasswordAsync(string userId, ChangePasswordRequestDto dto);
    Task<Result> UpdateProfileAsync(string userId, UpdateProfileRequestDto dto, string? imagePath);
    Task<ApplicationUser?> GetByIdAsync(string userId);
    Task<UserDto> ToUserDtoAsync(ApplicationUser user);
}

public class AuthService : IAuthService
{
    private const string TicketPurpose = "BarberHub.Registration.Ticket.v1";
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(15);

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly IOtpService _otp;
    private readonly IConfiguration _configuration;
    private readonly ITimeLimitedDataProtector _protector;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        IOtpService otp,
        IConfiguration configuration,
        IDataProtectionProvider dataProtection,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _emailService = emailService;
        _otp = otp;
        _configuration = configuration;
        _protector = dataProtection.CreateProtector(TicketPurpose).ToTimeLimitedDataProtector();
        _logger = logger;
    }

    public async Task<Result> ValidateRegistrationAsync(RegisterRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            return Result.Failure("Email is required.");

        var byEmail = await _userManager.FindByEmailAsync(dto.Email.Trim());
        if (byEmail is not null)
            return Result.Failure("A user with this email already exists.");

        if (!string.IsNullOrWhiteSpace(dto.PhoneNumber))
        {
            var byPhone = _userManager.Users.FirstOrDefault(u => u.PhoneNumber == dto.PhoneNumber.Trim());
            if (byPhone is not null)
                return Result.Failure("A user with this mobile number already exists.");
        }

        return Result.Success();
    }

    public string CreateRegistrationTicket(RegisterRequestDto dto)
    {
        var json = JsonSerializer.Serialize(dto);
        return _protector.Protect(json, TicketLifetime);
    }

    public RegisterRequestDto? ReadRegistrationTicket(string ticket)
    {
        try
        {
            var json = _protector.Unprotect(ticket);
            return JsonSerializer.Deserialize<RegisterRequestDto>(json);
        }
        catch
        {
            return null; // expired or tampered
        }
    }

    public async Task<Result<ApplicationUser>> CompleteRegistrationAsync(RegisterRequestDto dto)
    {
        var pre = await ValidateRegistrationAsync(dto);
        if (!pre.IsSuccess) return Result<ApplicationUser>.Failure(pre.Error!);

        var normalizedEmail = dto.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = normalizedEmail,
            Email = normalizedEmail,
            PhoneNumber = string.IsNullOrWhiteSpace(dto.PhoneNumber) ? null : dto.PhoneNumber.Trim(),
            FullName = dto.FullName.Trim(),
            City = dto.City?.Trim(),
            EmailConfirmed = true,
            PhoneNumberConfirmed = !string.IsNullOrWhiteSpace(dto.PhoneNumber),
            ShopName = dto.IsBarber ? dto.ShopName?.Trim() : null,
            ShopDescription = dto.IsBarber ? dto.ShopDescription?.Trim() : null,
            Address = dto.Address?.Trim(),
            OpeningTime = dto.IsBarber ? new TimeOnly(9, 0) : null,
            ClosingTime = dto.IsBarber ? new TimeOnly(20, 0) : null,
            WeeklyHoliday = dto.IsBarber ? DayOfWeek.Sunday : null,
            ApprovalStatus = dto.IsBarber ? BarberApprovalStatus.Pending : BarberApprovalStatus.NotApplicable,
            ApprovalRequestedAt = dto.IsBarber ? DateTime.UtcNow : null
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            return Result<ApplicationUser>.Failure(result.Errors.Select(e => e.Description).ToList());

        var role = dto.IsBarber ? AppRoles.Admin : AppRoles.User;
        await _userManager.AddToRoleAsync(user, role);

        return Result<ApplicationUser>.Success(user);
    }

    public async Task<Result<ApplicationUser>> ValidateLoginAsync(LoginRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.EmailOrPhone))
            return Result<ApplicationUser>.Failure("Email is required.");

        var input = dto.EmailOrPhone.Trim();
        if (!input.Contains('@'))
            return Result<ApplicationUser>.Failure("Please log in using your email address. Phone number login is not supported.");

        ApplicationUser? user = await _userManager.FindByEmailAsync(input);

        if (user is null)
            return Result<ApplicationUser>.Failure("Invalid credentials.");

        if (user.IsDeleted || !user.IsActive)
            return Result<ApplicationUser>.Failure("Your account is deactivated.");

        if (await _userManager.IsLockedOutAsync(user))
            return Result<ApplicationUser>.Failure("Account locked. Try again later.");

        var passwordOk = await _userManager.CheckPasswordAsync(user, dto.Password);
        if (!passwordOk)
        {
            await _userManager.AccessFailedAsync(user);
            return Result<ApplicationUser>.Failure("Invalid credentials.");
        }

        await _userManager.ResetAccessFailedCountAsync(user);
        return Result<ApplicationUser>.Success(user);
    }

    public async Task<Result> ForgotPasswordAsync(string email)
    {
        var user = await _userManager.FindByEmailAsync(email.Trim());
        if (user is null) return Result.Success(); // don't reveal existence

        await _otp.IssueAsync(user.Email!, OtpChannel.Email, OtpPurpose.PasswordReset);
        _logger.LogInformation("Password reset OTP code issued for {Email}", email);

        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordRequestDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email.Trim());
        if (user is null) return Result.Failure("Invalid request.");

        // First check if token is a 6-digit OTP code
        var otpCheck = await _otp.VerifyAsync(dto.Email.Trim(), dto.Token.Trim(), OtpPurpose.PasswordReset, OtpChannel.Email);
        if (otpCheck.Ok)
        {
            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, resetToken, dto.NewPassword);
            return result.Succeeded
                ? Result.Success()
                : Result.Failure(result.Errors.Select(e => e.Description).ToList());
        }

        // Fallback: verify as Identity token directly
        var directResult = await _userManager.ResetPasswordAsync(user, dto.Token.Trim(), dto.NewPassword);
        return directResult.Succeeded
            ? Result.Success()
            : Result.Failure(otpCheck.Error ?? directResult.Errors.FirstOrDefault()?.Description ?? "Invalid or expired reset code.");
    }

    public async Task<Result> ChangePasswordAsync(string userId, ChangePasswordRequestDto dto)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return Result.Failure("User not found.");

        var result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
        return result.Succeeded
            ? Result.Success()
            : Result.Failure(result.Errors.Select(e => e.Description).ToList());
    }

    public async Task<Result> UpdateProfileAsync(string userId, UpdateProfileRequestDto dto, string? imagePath)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return Result.Failure("User not found.");

        user.FullName = dto.FullName;
        user.PhoneNumber = dto.PhoneNumber;
        user.City = dto.City;
        user.Address = dto.Address;
        user.Latitude = dto.Latitude;
        user.Longitude = dto.Longitude;
        user.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(imagePath))
            user.ProfileImageUrl = imagePath;

        if (await _userManager.IsInRoleAsync(user, AppRoles.Admin))
        {
            user.ShopName = dto.ShopName;
            user.ShopDescription = dto.ShopDescription;
            user.WeeklyHoliday = dto.WeeklyHoliday;
            user.OpeningTime = dto.OpeningTime;
            user.ClosingTime = dto.ClosingTime;
        }

        var result = await _userManager.UpdateAsync(user);
        return result.Succeeded
            ? Result.Success()
            : Result.Failure(result.Errors.Select(e => e.Description).ToList());
    }

    public Task<ApplicationUser?> GetByIdAsync(string userId) => _userManager.FindByIdAsync(userId);

    public async Task<UserDto> ToUserDtoAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        return new UserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            Role = roles.FirstOrDefault() ?? AppRoles.User,
            ProfileImageUrl = user.ProfileImageUrl,
            City = user.City,
            Address = user.Address,
            Latitude = user.Latitude,
            Longitude = user.Longitude,
            ShopName = user.ShopName,
            ShopDescription = user.ShopDescription,
            ApprovalStatus = user.ApprovalStatus.ToString(),
            ApprovalRejectionReason = user.ApprovalRejectionReason,
            WeeklyHoliday = user.WeeklyHoliday,
            OpeningTime = user.OpeningTime,
            ClosingTime = user.ClosingTime,
            TwoFactorEnabled = _configuration.GetValue<bool>("Features:SuperAdminTwoFactorEnabled")
                && await _userManager.GetTwoFactorEnabledAsync(user)
        };
    }
}
