using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[ModuleAuthorize(AppModules.Duyurular)]
public class AnnouncementsController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    NotificationService notificationService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var rows = await db.Announcements.AsNoTracking()
            .OrderByDescending(x => x.PublishDate)
            .ToListAsync();
        return View(rows);
    }

    public IActionResult Create() => View(new Announcement { PublishDate = DateTime.Today, IsPublished = true });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Announcement model)
    {
        if (!ModelState.IsValid) return View(model);
        model.PublishDate = DateTime.SpecifyKind(model.PublishDate, DateTimeKind.Utc);
        db.Announcements.Add(model);
        await db.SaveChangesAsync();

        if (model.IsPublished)
        {
            await NotifyAllUsersAsync(model);
        }

        TempData["ActionSuccess"] = "Duyuru oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var entity = await db.Announcements.FindAsync(id);
        return entity is null ? NotFound() : View(entity);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Announcement model)
    {
        if (!ModelState.IsValid) return View(model);
        var entity = await db.Announcements.FindAsync(model.Id);
        if (entity is null) return NotFound();

        var wasPublished = entity.IsPublished;

        entity.Title = model.Title.Trim();
        entity.Body = model.Body.Trim();
        entity.Priority = model.Priority.Trim();
        entity.PublishDate = DateTime.SpecifyKind(model.PublishDate, DateTimeKind.Utc);
        entity.IsPublished = model.IsPublished;
        await db.SaveChangesAsync();

        // Sadece yayinlanmamis -> yayinlanmis gecisinde bildirim gonderilir (her duzenlemede tekrar spam olmasin).
        if (!wasPublished && entity.IsPublished)
        {
            await NotifyAllUsersAsync(entity);
        }

        TempData["ActionSuccess"] = "Duyuru güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    // Duyuru yayina girince tum kayitli kullanicilara (Sakin dahil) bildirim + best-effort push gonderir.
    private async Task NotifyAllUsersAsync(Announcement announcement)
    {
        var userIds = await userManager.Users.Select(x => x.Id).ToListAsync();
        foreach (var userId in userIds)
        {
            await notificationService.NotifyAsync(
                userId,
                NotificationType.Duyuru,
                announcement.Title,
                announcement.Body,
                $"/m/Duyurular/Detay/{announcement.Id}");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await db.Announcements.FindAsync(id);
        if (entity is null)
        {
            TempData["ActionError"] = "Duyuru bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        db.Announcements.Remove(entity);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Duyuru silindi.";
        return RedirectToAction(nameof(Index));
    }
}
