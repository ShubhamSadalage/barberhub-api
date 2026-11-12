using BarberHub.Api.Domain.Constants;
using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Features.Notifications;
using BarberHub.Api.Infrastructure.Persistence;
using BarberHub.Api.Shared.Services;
using BarberHub.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Xunit;

namespace BarberHub.Api.Tests.Notifications;

public class NotificationServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<UserManager<ApplicationUser>> _userManagerMock;
    private readonly Mock<IHubContext<NotificationHub>> _hubMock;
    private readonly Mock<IClientProxy> _clientProxyMock;
    private readonly Mock<IPushService> _pushMock;
    private readonly List<ApplicationUser> _users;
    private readonly NotificationService _sut;

    public NotificationServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _users = new List<ApplicationUser>();
        _userManagerMock = MockHelpers.CreateUserManager(_users);

        _hubMock = new Mock<IHubContext<NotificationHub>>();
        var hubClients = new Mock<IHubClients>();
        _clientProxyMock = new Mock<IClientProxy>();
        _hubMock.Setup(h => h.Clients).Returns(hubClients.Object);
        hubClients.Setup(c => c.User(It.IsAny<string>())).Returns(_clientProxyMock.Object);

        _pushMock = new Mock<IPushService>();

        _sut = new NotificationService(
            _db,
            _userManagerMock.Object,
            _hubMock.Object,
            _pushMock.Object);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task NotifyAsync_SavesNotificationToDb_AndDispatchesSignalRAndPush()
    {
        // Arrange
        var userId = "user-123";
        var type = NotificationType.BookingAccepted;
        var title = "Appointment Confirmed!";
        var msg = "Your haircut slot is locked in.";
        var link = "/Bookings/MyBookings";

        // Act
        await _sut.NotifyAsync(userId, type, title, msg, link);

        // Assert
        var savedNotification = _db.Notifications.FirstOrDefault(n => n.RecipientUserId == userId);
        savedNotification.Should().NotBeNull();
        savedNotification!.Title.Should().Be(title);
        savedNotification.Message.Should().Be(msg);
        savedNotification.Type.Should().Be(type);
        savedNotification.LinkUrl.Should().Be(link);
        savedNotification.IsRead.Should().BeFalse();

        // SignalR dispatch verified
        _clientProxyMock.Verify(c => c.SendCoreAsync(
            "ReceiveNotification",
            It.Is<object[]>(args => args.Length == 1 && ((NotificationDto)args[0]).Title == title),
            default), Times.Once);

        // Push dispatch verified
        _pushMock.Verify(p => p.SendToUserAsync(
            userId,
            It.Is<PushPayload>(payload => payload.Title == title && payload.Body == msg)),
            Times.Once);
    }

    [Fact]
    public async Task NotifyAllSuperAdminsAsync_BroadcastsToEverySuperAdmin()
    {
        // Arrange
        var sa1 = new ApplicationUser { Id = "sa-1", FullName = "Admin One" };
        var sa2 = new ApplicationUser { Id = "sa-2", FullName = "Admin Two" };
        _users.AddRange(new[] { sa1, sa2 });

        _userManagerMock.Setup(m => m.GetUsersInRoleAsync(AppRoles.SuperAdmin))
            .ReturnsAsync(new List<ApplicationUser> { sa1, sa2 });

        // Act
        await _sut.NotifyAllSuperAdminsAsync(
            NotificationType.BarberRegistration,
            "New Barber Application",
            "A barber has registered.");

        // Assert
        _db.Notifications.Count(n => n.RecipientUserId == sa1.Id).Should().Be(1);
        _db.Notifications.Count(n => n.RecipientUserId == sa2.Id).Should().Be(1);
    }

    [Fact]
    public async Task NotificationsController_RecentAndMarkRead_WorksCorrectly()
    {
        // Arrange
        var userId = "test-user";
        var n1 = new Notification
        {
            Id = Guid.NewGuid(),
            RecipientUserId = userId,
            Title = "Notif 1",
            Message = "Msg 1",
            IsRead = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        };
        var n2 = new Notification
        {
            Id = Guid.NewGuid(),
            RecipientUserId = userId,
            Title = "Notif 2",
            Message = "Msg 2",
            IsRead = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-2)
        };
        _db.Notifications.AddRange(n1, n2);
        await _db.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(c => c.UserId).Returns(userId);

        var controller = new NotificationsController(_db, currentUserMock.Object, _pushMock.Object);

        // Act 1: Get recent
        var recentResult = await controller.Recent();
        var okResult = recentResult.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();

        // Act 2: Mark n1 read
        await controller.MarkRead(n1.Id);

        // Assert
        var reloadedN1 = await _db.Notifications.FindAsync(n1.Id);
        reloadedN1!.IsRead.Should().BeTrue();

        var reloadedN2 = await _db.Notifications.FindAsync(n2.Id);
        reloadedN2!.IsRead.Should().BeFalse();

        // Act 3: Mark all read
        await controller.MarkAllRead();
        reloadedN2 = await _db.Notifications.FindAsync(n2.Id);
        reloadedN2!.IsRead.Should().BeTrue();
    }
}
