using System.Security.Claims;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Areas.Mobile.Controllers;

[Area("Mobile")]
[Authorize]
public class BildirimlerController(
    NotificationService notificationService,
    ApplicationDbContext db) : Controller
{
    private string? CurrentUserId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public async Task<IActionResult> Index()
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Challenge();
        }

        var rows = await notificationService.GetForUserAsync(userId);
        return View(rows.Select(x => new MobileNotificationRow
        {
            Id = x.Id,
            Type = x.Type,
            Title = x.Title,
            Body = x.Body,
            LinkUrl = x.LinkUrl,
            CreatedAt = x.CreatedAt,
            IsRead = x.ReadAt.HasValue
        }).ToList());
    }

    // Tarayici Push API aboneligini kaydeder/gunceller (Anlik bildirimleri ac butonu).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Abone(string endpoint, string p256dh, string auth)
    {
        var userId = CurrentUserId;
        if (userId is null || string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(p256dh) || string.IsNullOrWhiteSpace(auth))
        {
            return BadRequest();
        }

        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(x => x.Endpoint == endpoint);
        if (existing is null)
        {
            db.PushSubscriptions.Add(new Models.PushSubscription
            {
                UserId = userId,
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth,
                UserAgent = Request.Headers.UserAgent.ToString()
            });
        }
        else
        {
            existing.UserId = userId;
            existing.P256dh = p256dh;
            existing.Auth = auth;
            existing.UserAgent = Request.Headers.UserAgent.ToString();
            existing.FailCount = 0;
        }

        await db.SaveChangesAsync();
        return Ok();
    }

    // Push aboneligini kaldirir (kullanici anlik bildirimleri kapatinca).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AbonelikSil(string endpoint)
    {
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            var existing = await db.PushSubscriptions.FirstOrDefaultAsync(x => x.Endpoint == endpoint);
            if (existing is not null)
            {
                db.PushSubscriptions.Remove(existing);
                await db.SaveChangesAsync();
            }
        }

        return Ok();
    }

    // Zil rozeti icin JSON polling ucnoktasi (60 sn'de bir cagrilir).
    [HttpGet]
    public async Task<IActionResult> Ozet()
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Json(new { okunmamis = 0 });
        }

        var count = await notificationService.GetUnreadCountAsync(userId);
        return Json(new { okunmamis = count });
    }

    // Bildirime tiklama: okundu isaretler ve LinkUrl'e yonlendirir.
    public async Task<IActionResult> Ac(int id)
    {
        var userId = CurrentUserId;
        if (userId is null)
        {
            return Challenge();
        }

        var link = await notificationService.MarkReadAsync(id, userId);
        if (string.IsNullOrWhiteSpace(link))
        {
            return RedirectToAction(nameof(Index));
        }

        return Redirect(link);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TumunuOku()
    {
        var userId = CurrentUserId;
        if (userId is not null)
        {
            await notificationService.MarkAllReadAsync(userId);
        }

        return RedirectToAction(nameof(Index));
    }
}
