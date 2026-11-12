using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Features.Notifications;
using BarberHub.Api.Infrastructure.Persistence;
using BarberHub.Api.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BarberHub.Api.Features.Chat;

public class ChatMessageDto
{
    public Guid Id { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string ReceiverId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public bool IsRead { get; set; }
}

public class ChatContactDto
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
    public string? LastMessage { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public int UnreadCount { get; set; }
}

/// <summary>
/// JWT-authenticated (via the same query-string bearer token wiring as NotificationHub —
/// see Program.cs JwtBearerEvents.OnMessageReceived for /hubs/chat). The RN client calls
/// connection.invoke("SendMessage", receiverId, content) directly; there's no REST POST
/// for sending, matching the original SignalR-only send path.
/// </summary>
[Authorize]
public class ChatHub : Hub
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notifications;

    public ChatHub(ApplicationDbContext context, INotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    public async Task SendMessage(string receiverId, string content)
    {
        var senderId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(senderId) || string.IsNullOrEmpty(receiverId) || string.IsNullOrWhiteSpace(content))
            return;

        var message = new ChatMessage
        {
            SenderId = senderId,
            ReceiverId = receiverId,
            Content = content.Trim(),
            SentAt = DateTime.UtcNow
        };

        await _context.ChatMessages.AddAsync(message);
        await _context.SaveChangesAsync();

        var sender = await _context.Users.FindAsync(senderId);

        var dto = new ChatMessageDto
        {
            Id = message.Id,
            SenderId = senderId,
            SenderName = sender?.FullName ?? "",
            ReceiverId = receiverId,
            Content = message.Content,
            SentAt = message.SentAt
        };

        await Clients.User(receiverId).SendAsync("ReceiveMessage", dto);
        await Clients.Caller.SendAsync("MessageSent", dto);

        // In-app + push notification so the recipient sees a badge/toast even with the
        // chat screen closed (handled by NotificationService: SignalR + FCM together).
        var preview = message.Content.Length > 80 ? message.Content[..80] + "…" : message.Content;
        await _notifications.NotifyAsync(
            receiverId,
            NotificationType.NewMessage,
            $"New message from {sender?.FullName ?? "someone"}",
            preview,
            $"Chat/Conversation?otherUserId={senderId}");
    }

    /// <summary>Called by the client when the user opens a conversation, so the sender's
    /// "seen" tick can update in real time without a page refresh.</summary>
    public async Task MarkRead(string otherUserId)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(userId)) return;

        var unread = await _context.ChatMessages
            .Where(m => m.SenderId == otherUserId && m.ReceiverId == userId && !m.IsRead)
            .ToListAsync();
        if (unread.Count == 0) return;

        foreach (var m in unread) m.IsRead = true;
        await _context.SaveChangesAsync();

        await Clients.User(otherUserId).SendAsync("MessagesRead", new { byUserId = userId });
    }
}

[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentUserService _currentUser;

    public ChatController(ApplicationDbContext context, ICurrentUserService currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    /// <summary>Inbox/contacts list — everyone the user has ever exchanged a message with, most recent first.</summary>
    [HttpGet("contacts")]
    public async Task<IActionResult> Contacts()
    {
        var userId = _currentUser.UserId!;

        var contactIds = await _context.ChatMessages
            .Where(m => m.SenderId == userId || m.ReceiverId == userId)
            .Select(m => m.SenderId == userId ? m.ReceiverId : m.SenderId)
            .Distinct()
            .ToListAsync();

        var contacts = new List<ChatContactDto>();
        foreach (var cid in contactIds)
        {
            var user = await _context.Users.FindAsync(cid);
            if (user is null) continue;

            var lastMsg = await _context.ChatMessages
                .Where(m => (m.SenderId == userId && m.ReceiverId == cid) || (m.SenderId == cid && m.ReceiverId == userId))
                .OrderByDescending(m => m.SentAt)
                .FirstOrDefaultAsync();

            var unread = await _context.ChatMessages
                .CountAsync(m => m.SenderId == cid && m.ReceiverId == userId && !m.IsRead);

            contacts.Add(new ChatContactDto
            {
                UserId = user.Id,
                FullName = user.ShopName ?? user.FullName,
                ProfileImageUrl = user.ProfileImageUrl,
                LastMessage = lastMsg?.Content,
                LastMessageAt = lastMsg?.SentAt,
                UnreadCount = unread
            });
        }

        return Ok(contacts.OrderByDescending(c => c.LastMessageAt).ToList());
    }

    /// <summary>Full message history with one contact. Marks incoming messages as read as a side effect (same as the original MVC action).</summary>
    [HttpGet("conversation/{otherUserId}")]
    public async Task<IActionResult> Conversation(string otherUserId)
    {
        var userId = _currentUser.UserId!;
        var other = await _context.Users.FindAsync(otherUserId);
        if (other is null) return NotFound();

        var messages = await _context.ChatMessages
            .Where(m => (m.SenderId == userId && m.ReceiverId == otherUserId)
                     || (m.SenderId == otherUserId && m.ReceiverId == userId))
            .OrderBy(m => m.SentAt)
            .ToListAsync();

        foreach (var msg in messages.Where(m => m.ReceiverId == userId && !m.IsRead))
            msg.IsRead = true;
        await _context.SaveChangesAsync();

        return Ok(new
        {
            otherUser = new { other.Id, FullName = other.ShopName ?? other.FullName, other.ProfileImageUrl },
            messages = messages.Select(m => new ChatMessageDto
            {
                Id = m.Id,
                SenderId = m.SenderId,
                ReceiverId = m.ReceiverId,
                Content = m.Content,
                SentAt = m.SentAt,
                IsRead = m.IsRead
            })
        });
    }
}
