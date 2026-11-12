using BarberHub.Api.Domain.Constants;
using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Infrastructure.Persistence;
using BarberHub.Api.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BarberHub.Api.Features.Notifications;

// =================== DTOs ===================

public class NotificationDto
{
    public Guid Id { get; set; }
    public NotificationType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? LinkUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class RegisterDeviceRequestDto
{
    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = "android";
    public string? DeviceModel { get; set; }
}

public class UnregisterDeviceRequestDto
{
    public string Token { get; set; } = string.Empty;
}

// =================== Service ===================

public interface INotificationService
{
    Task NotifyAsync(string recipientUserId, NotificationType type, string title, string message, string? linkUrl = null);
    Task NotifyAllSuperAdminsAsync(NotificationType type, string title, string message, string? linkUrl = null);
}

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly IPushService _push;

    public NotificationService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IHubContext<NotificationHub> hub,
        IPushService push)
    {
        _db = db; _userManager = userManager; _hub = hub; _push = push;
    }

    public async Task NotifyAsync(string recipientUserId, NotificationType type, string title, string message, string? linkUrl = null)
    {
        var n = new Notification
        {
            RecipientUserId = recipientUserId,
            Type = type,
            Title = title,
            Message = message,
            LinkUrl = linkUrl
        };
        await _db.Notifications.AddAsync(n);
        await _db.SaveChangesAsync();

        // 1) SignalR — live update while the app is foregrounded/connected
        await _hub.Clients.User(recipientUserId).SendAsync("ReceiveNotification", new NotificationDto
        {
            Id = n.Id, Type = n.Type, Title = n.Title, Message = n.Message,
            LinkUrl = n.LinkUrl, IsRead = false, CreatedAt = n.CreatedAt
        });

        // 2) FCM push — works in background / app closed / device locked
        try
        {
            await _push.SendToUserAsync(recipientUserId, new PushPayload
            {
                Title = title, Body = message, Url = linkUrl, Tag = type.ToString()
            });
        }
        catch
        {
            // Push failure must never break in-app notifications
        }
    }

    public async Task NotifyAllSuperAdminsAsync(NotificationType type, string title, string message, string? linkUrl = null)
    {
        var superAdmins = await _userManager.GetUsersInRoleAsync(AppRoles.SuperAdmin);
        foreach (var sa in superAdmins)
            await NotifyAsync(sa.Id, type, title, message, linkUrl);
    }
}

// =================== SignalR Hub ===================

/// <summary>
/// JWT-authenticated hub. The access token is passed as a query string param
/// (?access_token=...) because RN's SignalR client (like browser EventSource/WebSocket)
/// can't set an Authorization header during the initial handshake — Program.cs wires
/// this up via JwtBearerEvents.OnMessageReceived.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
}

// =================== Controller (bell icon + device registration) ===================

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IPushService _push;

    public NotificationsController(ApplicationDbContext db, ICurrentUserService currentUser, IPushService push)
    {
        _db = db; _currentUser = currentUser; _push = push;
    }

    [HttpGet("recent")]
    public async Task<IActionResult> Recent()
    {
        var userId = _currentUser.UserId!;
        var items = await _db.Notifications
            .Where(n => n.RecipientUserId == userId && !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .Take(20)
            .Select(n => new NotificationDto
            {
                Id = n.Id, Type = n.Type, Title = n.Title, Message = n.Message,
                LinkUrl = n.LinkUrl, IsRead = n.IsRead, CreatedAt = n.CreatedAt
            })
            .ToListAsync();

        return Ok(new { items, unreadCount = items.Count });
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var userId = _currentUser.UserId!;
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.RecipientUserId == userId);
        if (n is not null && !n.IsRead)
        {
            n.IsRead = true;
            n.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = _currentUser.UserId!;
        var unread = await _db.Notifications.Where(n => n.RecipientUserId == userId && !n.IsRead).ToListAsync();
        foreach (var n in unread) { n.IsRead = true; n.ReadAt = DateTime.UtcNow; }
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Called by the RN app right after it gets an FCM token (on launch + on token refresh).</summary>
    [HttpPost("devices")]
    public async Task<IActionResult> RegisterDevice(RegisterDeviceRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Token)) return BadRequest("Token is required.");
        await _push.SaveDeviceTokenAsync(_currentUser.UserId!, dto.Token, dto.Platform, dto.DeviceModel);
        return Ok(new { ok = true });
    }

    [HttpPost("devices/unregister")]
    public async Task<IActionResult> UnregisterDevice(UnregisterDeviceRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Token)) return BadRequest("Token is required.");
        await _push.RemoveDeviceTokenAsync(dto.Token);
        return Ok(new { ok = true });
    }
}
