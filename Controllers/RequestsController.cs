using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[ModuleAuthorize(AppModules.Talepler)]
public class RequestsController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    NotificationService notificationService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var rows = await db.ServiceRequests.AsNoTracking()
            .Include(x => x.Unit).ThenInclude(x => x!.Block)
            .OrderBy(x => x.Status == ServiceRequestStatus.Closed)
            .ThenByDescending(x => x.Priority)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync();
        return View(rows);
    }

    public async Task<IActionResult> Create()
    {
        await FillOptionsAsync();
        return View(new ServiceRequest { CreatedAt = DateTime.Today, Status = ServiceRequestStatus.Open });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ServiceRequest model)
    {
        ModelState.Remove(nameof(ServiceRequest.Unit));
        if (!ModelState.IsValid)
        {
            await FillOptionsAsync(model.AssignedToUserId);
            return View(model);
        }

        model.CreatedAt = DateTime.SpecifyKind(model.CreatedAt, DateTimeKind.Utc);
        model.DueDate = model.DueDate.HasValue ? DateTime.SpecifyKind(model.DueDate.Value, DateTimeKind.Utc) : null;
        await ApplyAssignmentAsync(model, model.AssignedToUserId);

        db.ServiceRequests.Add(model);
        await db.SaveChangesAsync();

        if (!string.IsNullOrEmpty(model.AssignedToUserId))
        {
            await NotifyAssignmentAsync(model);
        }

        TempData["ActionSuccess"] = "Talep oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var entity = await db.ServiceRequests.FindAsync(id);
        if (entity is null) return NotFound();
        await FillOptionsAsync(entity.AssignedToUserId);
        return View(entity);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ServiceRequest model)
    {
        ModelState.Remove(nameof(ServiceRequest.Unit));
        if (!ModelState.IsValid)
        {
            await FillOptionsAsync(model.AssignedToUserId);
            return View(model);
        }

        var entity = await db.ServiceRequests.FindAsync(model.Id);
        if (entity is null) return NotFound();

        var previousAssignedToUserId = entity.AssignedToUserId;

        entity.Title = model.Title.Trim();
        entity.Description = model.Description;
        entity.UnitId = model.UnitId;
        entity.Status = model.Status;
        entity.Priority = model.Priority;
        entity.IsVisibleToResidents = model.IsVisibleToResidents;
        entity.CreatedAt = DateTime.SpecifyKind(model.CreatedAt, DateTimeKind.Utc);
        entity.DueDate = model.DueDate.HasValue ? DateTime.SpecifyKind(model.DueDate.Value, DateTimeKind.Utc) : null;
        entity.ResolvedAt = model.Status is ServiceRequestStatus.Resolved or ServiceRequestStatus.Closed
            ? (entity.ResolvedAt ?? DateTime.UtcNow)
            : null;

        await ApplyAssignmentAsync(entity, model.AssignedToUserId);
        await db.SaveChangesAsync();

        var assignmentChanged = !string.IsNullOrEmpty(entity.AssignedToUserId) && entity.AssignedToUserId != previousAssignedToUserId;
        if (assignmentChanged)
        {
            await NotifyAssignmentAsync(entity);
        }

        TempData["ActionSuccess"] = "Talep güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await db.ServiceRequests.FindAsync(id);
        if (entity is null)
        {
            TempData["ActionError"] = "Talep bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        db.ServiceRequests.Remove(entity);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Talep silindi.";
        return RedirectToAction(nameof(Index));
    }

    private async Task FillOptionsAsync(string? selectedAssignedToUserId = null)
    {
        ViewBag.UnitOptions = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .Where(x => x.Active)
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .Select(x => new SelectListItem(UnitDisplayHelper.Display(x), x.Id.ToString()))
            .ToListAsync();

        ViewBag.AssignableUserOptions = await AssignableUserHelper.BuildOptionsAsync(userManager, selectedAssignedToUserId);
    }

    // Secilen kullaniciyi hem AssignedToUserId'ye hem gorunen ad olarak AssignedTo'ya yazar.
    private async Task ApplyAssignmentAsync(ServiceRequest entity, string? assignedToUserId)
    {
        entity.AssignedToUserId = string.IsNullOrEmpty(assignedToUserId) ? null : assignedToUserId;
        if (entity.AssignedToUserId is null)
        {
            entity.AssignedTo = null;
            return;
        }

        var user = await userManager.FindByIdAsync(entity.AssignedToUserId);
        if (user is not null)
        {
            entity.AssignedTo = AssignableUserHelper.DisplayName(user);
        }
    }

    private async Task NotifyAssignmentAsync(ServiceRequest entity)
    {
        if (string.IsNullOrEmpty(entity.AssignedToUserId))
        {
            return;
        }

        await notificationService.NotifyAsync(
            entity.AssignedToUserId,
            NotificationType.TalepAtama,
            $"Size yeni talep atandı: {entity.Title}",
            entity.Description,
            $"/m/Talepler/Detay/{entity.Id}");
    }
}
