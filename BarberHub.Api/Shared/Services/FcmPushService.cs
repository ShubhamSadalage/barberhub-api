using BarberHub.Api.Infrastructure.Persistence;
using BarberHub.Api.Shared.Config;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BarberHub.Api.Shared.Services;

public class PushPayload
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Url { get; set; }        // deep-link route inside the RN app, e.g. "Bookings/MyBookings"
    public string? Tag { get; set; }        // used as a data field for client-side dedup/grouping
}

public interface IPushService
{
    Task<int> SendToUserAsync(string userId, PushPayload payload);
    Task SaveDeviceTokenAsync(string userId, string token, string platform, string? deviceModel);
    Task RemoveDeviceTokenAsync(string token);
    bool IsConfigured();
}

/// <summary>
/// Sends push notifications to Android devices via Firebase Cloud Messaging.
/// Configure Firebase:ServiceAccountJsonPath (or Firebase:ServiceAccountJson inline)
/// in appsettings / user-secrets, pointing at the downloaded service-account key
/// from the Firebase console (Project settings → Service accounts).
/// </summary>
public class FcmPushService : IPushService
{
    private static readonly object InitLock = new();
    private static FirebaseApp? _app;

    private readonly ApplicationDbContext _db;
    private readonly FirebaseOptions _opts;
    private readonly FeatureFlags _features;
    private readonly ILogger<FcmPushService> _logger;

    public FcmPushService(
        ApplicationDbContext db,
        IOptions<FirebaseOptions> opts,
        IOptions<FeatureFlags> features,
        ILogger<FcmPushService> logger)
    {
        _db = db;
        _opts = opts.Value;
        _features = features.Value;
        _logger = logger;
        EnsureInitialized();
    }

    public bool IsConfigured() => _features.PushEnabled && _app is not null;

    private void EnsureInitialized()
    {
        if (_app is not null || !_features.PushEnabled) return;
        lock (InitLock)
        {
            if (_app is not null) return;
            try
            {
                GoogleCredential credential;
                if (!string.IsNullOrEmpty(_opts.ServiceAccountJson))
                    credential = GoogleCredential.FromJson(_opts.ServiceAccountJson);
                else if (!string.IsNullOrEmpty(_opts.ServiceAccountJsonPath) && File.Exists(_opts.ServiceAccountJsonPath))
                    credential = GoogleCredential.FromFile(_opts.ServiceAccountJsonPath);
                else
                {
                    _logger.LogWarning("Firebase not configured — push notifications disabled.");
                    return;
                }

                _app = FirebaseApp.Create(new AppOptions { Credential = credential });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize FirebaseApp for FCM push.");
            }
        }
    }

    public async Task<int> SendToUserAsync(string userId, PushPayload payload)
    {
        if (!IsConfigured()) return 0;

        var tokens = await _db.PushSubscriptions
            .Where(s => s.UserId == userId && s.IsActive)
            .ToListAsync();
        if (tokens.Count == 0) return 0;

        var messaging = FirebaseMessaging.DefaultInstance;
        int sent = 0;

        foreach (var t in tokens)
        {
            var message = new Message
            {
                Token = t.DeviceToken,
                Notification = new FirebaseAdmin.Messaging.Notification
                {
                    Title = payload.Title,
                    Body = payload.Body
                },
                Data = new Dictionary<string, string>
                {
                    ["url"] = payload.Url ?? "",
                    ["tag"] = payload.Tag ?? Guid.NewGuid().ToString("N")
                },
                Android = new AndroidConfig
                {
                    Priority = Priority.High,
                    Notification = new AndroidNotification { ChannelId = "barberhub_default" }
                }
            };

            try
            {
                await messaging.SendAsync(message);
                t.LastSeenAt = DateTime.UtcNow;
                sent++;
            }
            catch (FirebaseMessagingException ex) when (
                ex.MessagingErrorCode == MessagingErrorCode.Unregistered ||
                ex.MessagingErrorCode == MessagingErrorCode.InvalidArgument)
            {
                // Token no longer valid (app uninstalled, token rotated, etc.)
                t.IsActive = false;
                _logger.LogInformation("Deactivated dead FCM token for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FCM send failed for user {UserId}", userId);
            }
        }

        await _db.SaveChangesAsync();
        return sent;
    }

    public async Task SaveDeviceTokenAsync(string userId, string token, string platform, string? deviceModel)
    {
        var existing = await _db.PushSubscriptions.FirstOrDefaultAsync(s => s.DeviceToken == token);
        if (existing is not null)
        {
            existing.UserId = userId;
            existing.Platform = platform;
            existing.DeviceModel = deviceModel;
            existing.LastSeenAt = DateTime.UtcNow;
            existing.IsActive = true;
        }
        else
        {
            await _db.PushSubscriptions.AddAsync(new Domain.Entities.PushSubscription
            {
                UserId = userId,
                DeviceToken = token,
                Platform = platform,
                DeviceModel = deviceModel
            });
        }
        await _db.SaveChangesAsync();
    }

    public async Task RemoveDeviceTokenAsync(string token)
    {
        var sub = await _db.PushSubscriptions.FirstOrDefaultAsync(s => s.DeviceToken == token);
        if (sub is null) return;
        sub.IsActive = false;
        await _db.SaveChangesAsync();
    }
}
