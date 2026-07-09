using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

/// <summary>
/// Kullaniciya ozel bildirim olusturma/okuma. Zil ikonu bu servis uzerinden calisir.
/// Push gonderimi (Asama 5) ayrica ve best-effort eklenecek; burada yalnizca DB kaydi tutulur.
/// </summary>
public sealed class NotificationService(ApplicationDbContext db)
{
    public async Task NotifyAsync(string recipientUserId, NotificationType type, string title, string? body, string? linkUrl)
    {
        if (string.IsNullOrWhiteSpace(recipientUserId))
        {
            return;
        }

        db.Notifications.Add(new Notification
        {
            RecipientUserId = recipientUserId,
            Type = type,
            Title = title,
            Body = body,
            LinkUrl = linkUrl
        });

        await db.SaveChangesAsync();
    }

    public Task<int> GetUnreadCountAsync(string userId)
        => db.Notifications.CountAsync(x => x.RecipientUserId == userId && x.ReadAt == null);

    public Task<List<Notification>> GetForUserAsync(string userId)
        => db.Notifications.AsNoTracking()
            .Where(x => x.RecipientUserId == userId)
            .OrderByDescending(x => x.ReadAt == null)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync();

    /// <summary>Bildirimi okundu isaretler; donen link (varsa) yonlendirme icin kullanilir.</summary>
    public async Task<string?> MarkReadAsync(int notificationId, string userId)
    {
        var notification = await db.Notifications
            .FirstOrDefaultAsync(x => x.Id == notificationId && x.RecipientUserId == userId);
        if (notification is null)
        {
            return null;
        }

        notification.ReadAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync();
        return notification.LinkUrl;
    }

    public async Task MarkAllReadAsync(string userId)
    {
        var unread = await db.Notifications
            .Where(x => x.RecipientUserId == userId && x.ReadAt == null)
            .ToListAsync();

        var now = DateTime.UtcNow;
        foreach (var n in unread)
        {
            n.ReadAt = now;
        }

        if (unread.Count > 0)
        {
            await db.SaveChangesAsync();
        }
    }
}
