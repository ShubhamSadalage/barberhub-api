using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Features.Auth.Dtos;
using BarberHub.Api.Features.Auth.Services;
using BarberHub.Api.Infrastructure.Persistence;
using BarberHub.Api.Shared.Config;
using BarberHub.Api.Shared.Services;
using BarberHub.Api.Tests.Helpers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BarberHub.Api.Tests.Auth;

public class EmailTemplateTests
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IEmailService> _emailMock;
    private readonly Mock<ISmsService> _smsMock;
    private readonly OtpService _otpService;

    public EmailTemplateTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);

        _emailMock = new Mock<IEmailService>();
        _emailMock.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                  .ReturnsAsync(true);

        _smsMock = new Mock<ISmsService>();

        var otpOptions = Options.Create(new OtpOptions { CodeLength = 6, ExpiryMinutes = 5, MaxAttempts = 5, ResendCooldownSeconds = 0 });
        var rateOptions = Options.Create(new RateLimitOptions { OtpPerHour = 100, LoginPerMinute = 100 });
        var smsOptions = Options.Create(new SmsOptions());
        var emailSettings = Options.Create(new EmailSettings { SmtpServer = "smtp.test.com" });

        _otpService = new OtpService(
            _db,
            otpOptions,
            rateOptions,
            smsOptions,
            emailSettings,
            _emailMock.Object,
            _smsMock.Object,
            NullLogger<OtpService>.Instance);
    }

    [Fact]
    public async Task IssueAsync_ForRegistrationOrLogin_SendsEmailWithBrandedTemplateAndCode()
    {
        // Act
        var result = await _otpService.IssueAsync("user@example.com", OtpChannel.Email, OtpPurpose.Login);

        // Assert
        Assert.True(result.Sent);
        _emailMock.Verify(e => e.SendAsync(
            "user@example.com",
            "Your Barber Hub Verification Code",
            It.Is<string>(html =>
                html.Contains("BARBER HUB") &&
                html.Contains("Verify Your Email") &&
                html.Contains("Your Verification Code") &&
                html.Contains("Expires in <strong>5 minutes</strong>") &&
                html.Contains("&copy;")
            )),
            Times.Once);
    }

    [Fact]
    public async Task IssueAsync_ForPasswordReset_SendsEmailWithPasswordResetTemplate()
    {
        // Act
        var result = await _otpService.IssueAsync("user@example.com", OtpChannel.Email, OtpPurpose.PasswordReset);

        // Assert
        Assert.True(result.Sent);
        _emailMock.Verify(e => e.SendAsync(
            "user@example.com",
            "Reset Your Password - Barber Hub",
            It.Is<string>(html =>
                html.Contains("BARBER HUB") &&
                html.Contains("Reset Your Password") &&
                html.Contains("Your Verification Code") &&
                html.Contains("We received a request to reset your password")
            )),
            Times.Once);
    }

    [Fact]
    public async Task ForgotPasswordAsync_TriggersOtpService_WithPasswordResetPurpose()
    {
        // Arrange
        var users = new List<ApplicationUser>
        {
            new ApplicationUser { Id = "u1", Email = "alex@test.com", NormalizedEmail = "ALEX@TEST.COM", FullName = "Alex" }
        };
        var userManagerMock = MockHelpers.CreateUserManager(users);
        var otpMock = new Mock<IOtpService>();
        otpMock.Setup(o => o.IssueAsync("alex@test.com", OtpChannel.Email, OtpPurpose.PasswordReset, null))
               .ReturnsAsync(new OtpIssueResult { Sent = true });

        var authService = new AuthService(
            userManagerMock.Object,
            _emailMock.Object,
            otpMock.Object,
            new Mock<Microsoft.Extensions.Configuration.IConfiguration>().Object,
            new EphemeralDataProtectionProvider(),
            NullLogger<AuthService>.Instance);

        // Act
        var result = await authService.ForgotPasswordAsync("alex@test.com");

        // Assert
        Assert.True(result.IsSuccess);
        otpMock.Verify(o => o.IssueAsync("alex@test.com", OtpChannel.Email, OtpPurpose.PasswordReset, null), Times.Once);
    }

    [Fact]
    public async Task ResetPasswordAsync_WithValidOtp_ResetsUserPassword()
    {
        // Arrange
        var user = new ApplicationUser { Id = "u1", Email = "alex@test.com", NormalizedEmail = "ALEX@TEST.COM", FullName = "Alex" };
        var users = new List<ApplicationUser> { user };
        var userManagerMock = MockHelpers.CreateUserManager(users);
        userManagerMock.Setup(m => m.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("GENERATED_TOKEN");
        userManagerMock.Setup(m => m.ResetPasswordAsync(user, "GENERATED_TOKEN", "NewPassword123!"))
                       .ReturnsAsync(IdentityResult.Success);

        var otpMock = new Mock<IOtpService>();
        otpMock.Setup(o => o.VerifyAsync("alex@test.com", "123456", OtpPurpose.PasswordReset, OtpChannel.Email))
               .ReturnsAsync(new OtpVerifyResult { Ok = true, UserId = "u1" });

        var authService = new AuthService(
            userManagerMock.Object,
            _emailMock.Object,
            otpMock.Object,
            new Mock<Microsoft.Extensions.Configuration.IConfiguration>().Object,
            new EphemeralDataProtectionProvider(),
            NullLogger<AuthService>.Instance);

        // Act
        var result = await authService.ResetPasswordAsync(new ResetPasswordRequestDto
        {
            Email = "alex@test.com",
            Token = "123456",
            NewPassword = "NewPassword123!"
        });

        // Assert
        Assert.True(result.IsSuccess);
        userManagerMock.Verify(m => m.ResetPasswordAsync(user, "GENERATED_TOKEN", "NewPassword123!"), Times.Once);
    }
}
