using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Features.Auth.Dtos;
using BarberHub.Api.Features.Auth.Services;
using BarberHub.Api.Shared.Services;
using BarberHub.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace BarberHub.Api.Tests.Auth;

public class AuthServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<IEmailService> _emailServiceMock;
    private readonly Mock<IOtpService> _otpMock;
    private readonly Mock<IConfiguration> _configMock;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly AuthService _sut;
    private readonly List<ApplicationUser> _users;

    public AuthServiceTests()
    {
        _users = new List<ApplicationUser>();
        _userManagerMock = MockHelpers.CreateUserManager(_users);
        _emailServiceMock = new Mock<IEmailService>();
        _otpMock = new Mock<IOtpService>();
        _configMock = new Mock<IConfiguration>();
        _dataProtectionProvider = new EphemeralDataProtectionProvider();

        _sut = new AuthService(
            _userManagerMock.Object,
            _emailServiceMock.Object,
            _otpMock.Object,
            _configMock.Object,
            _dataProtectionProvider,
            MockHelpers.CreateLogger<AuthService>());
    }

    [Fact]
    public async Task ValidateRegistrationAsync_WithUniqueEmailAndPhone_ReturnsSuccess()
    {
        // Arrange
        var dto = new RegisterRequestDto
        {
            Email = "newuser@test.com",
            PhoneNumber = "9998887770",
            FullName = "New User",
            Password = "Password@123"
        };

        // Act
        var result = await _sut.ValidateRegistrationAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateRegistrationAsync_WithDuplicateEmail_ReturnsFailure()
    {
        // Arrange
        _users.Add(new ApplicationUser
        {
            Id = "u1",
            Email = "existing@test.com",
            PhoneNumber = "1112223334",
            FullName = "Existing User"
        });

        var dto = new RegisterRequestDto
        {
            Email = "existing@test.com",
            PhoneNumber = "9998887770",
            FullName = "New User",
            Password = "Password@123"
        };

        // Act
        var result = await _sut.ValidateRegistrationAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("email already exists");
    }

    [Fact]
    public async Task ValidateRegistrationAsync_WithDuplicatePhone_ReturnsFailure()
    {
        // Arrange
        _users.Add(new ApplicationUser
        {
            Id = "u1",
            Email = "existing@test.com",
            PhoneNumber = "9998887770",
            FullName = "Existing User"
        });

        var dto = new RegisterRequestDto
        {
            Email = "newuser@test.com",
            PhoneNumber = "9998887770",
            FullName = "New User",
            Password = "Password@123"
        };

        // Act
        var result = await _sut.ValidateRegistrationAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("mobile number already exists");
    }

    [Fact]
    public void CreateAndReadRegistrationTicket_ValidDto_RoundtripsSuccessfully()
    {
        // Arrange
        var dto = new RegisterRequestDto
        {
            Email = "ticket@test.com",
            PhoneNumber = "1234567890",
            FullName = "Ticket User",
            Password = "Password@123",
            City = "Pune"
        };

        // Act
        var ticket = _sut.CreateRegistrationTicket(dto);
        var readDto = _sut.ReadRegistrationTicket(ticket);

        // Assert
        ticket.Should().NotBeNullOrWhiteSpace();
        readDto.Should().NotBeNull();
        readDto!.Email.Should().Be("ticket@test.com");
        readDto.FullName.Should().Be("Ticket User");
        readDto.City.Should().Be("Pune");
    }

    [Fact]
    public void ReadRegistrationTicket_InvalidOrTamperedTicket_ReturnsNull()
    {
        // Act
        var readDto = _sut.ReadRegistrationTicket("invalid-opaque-ticket-string");

        // Assert
        readDto.Should().BeNull();
    }

    [Fact]
    public async Task ValidateLoginAsync_ValidCredentials_ReturnsSuccess()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = "u1",
            Email = "login@test.com",
            UserName = "login@test.com",
            IsActive = true,
            IsDeleted = false
        };
        _users.Add(user);

        _userManagerMock.Setup(m => m.IsLockedOutAsync(user)).ReturnsAsync(false);
        _userManagerMock.Setup(m => m.CheckPasswordAsync(user, "ValidPassword@123")).ReturnsAsync(true);

        var dto = new LoginRequestDto
        {
            EmailOrPhone = "login@test.com",
            Password = "ValidPassword@123"
        };

        // Act
        var result = await _sut.ValidateLoginAsync(dto);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Email.Should().Be("login@test.com");
        _userManagerMock.Verify(m => m.ResetAccessFailedCountAsync(user), Times.Once);
    }

    [Fact]
    public async Task ValidateLoginAsync_WrongPassword_ReturnsFailureAndIncrementsFailedCount()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = "u1",
            Email = "login@test.com",
            UserName = "login@test.com",
            IsActive = true,
            IsDeleted = false
        };
        _users.Add(user);

        _userManagerMock.Setup(m => m.IsLockedOutAsync(user)).ReturnsAsync(false);
        _userManagerMock.Setup(m => m.CheckPasswordAsync(user, "WrongPassword")).ReturnsAsync(false);

        var dto = new LoginRequestDto
        {
            EmailOrPhone = "login@test.com",
            Password = "WrongPassword"
        };

        // Act
        var result = await _sut.ValidateLoginAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Invalid credentials.");
        _userManagerMock.Verify(m => m.AccessFailedAsync(user), Times.Once);
    }

    [Fact]
    public async Task ValidateLoginAsync_DeactivatedAccount_ReturnsFailure()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = "u1",
            Email = "deactive@test.com",
            UserName = "deactive@test.com",
            IsActive = false,
            IsDeleted = false
        };
        _users.Add(user);

        var dto = new LoginRequestDto
        {
            EmailOrPhone = "deactive@test.com",
            Password = "Password@123"
        };

        // Act
        var result = await _sut.ValidateLoginAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("deactivated");
    }

    [Fact]
    public async Task ValidateLoginAsync_LockedOutAccount_ReturnsFailure()
    {
        // Arrange
        var user = new ApplicationUser
        {
            Id = "u1",
            Email = "locked@test.com",
            UserName = "locked@test.com",
            IsActive = true,
            IsDeleted = false
        };
        _users.Add(user);

        _userManagerMock.Setup(m => m.IsLockedOutAsync(user)).ReturnsAsync(true);

        var dto = new LoginRequestDto
        {
            EmailOrPhone = "locked@test.com",
            Password = "Password@123"
        };

        // Act
        var result = await _sut.ValidateLoginAsync(dto);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("locked");
    }
}
