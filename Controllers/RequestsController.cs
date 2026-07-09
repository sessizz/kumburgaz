using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[ModuleAuthorize(AppModules.Talepler)]
public class RequestsController(ApplicationDbContext db) : Controller
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
            await FillOptionsAsync();
            return View(model);
        }

        model.CreatedAt = DateTime.SpecifyKind(model.CreatedAt, DateTimeKind.Utc);
        model.DueDate = model.DueDate.HasValue ? DateTime.SpecifyKind(model.DueDate.Value, DateTimeKind.Utc) : null;
        db.ServiceRequests.Add(model);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Talep oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var entity = await db.ServiceRequests.FindAsync(id);
        if (entity is null) return NotFound();
        await FillOptionsAsync();
        return View(entity);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ServiceRequest model)
    {
        ModelState.Remove(nameof(ServiceRequest.Unit));
        if (!ModelState.IsValid)
        {
            await FillOptionsAsync();
            return View(model);
        }

        var entity = await db.ServiceRequests.FindAsync(model.Id);
        if (entity is null) return NotFound();

        entity.Title = model.Title.Trim();
        entity.Description = model.Description;
        entity.UnitId = model.UnitId;
        entity.Status = model.Status;
        entity.Priority = model.Priority;
        entity.AssignedTo = model.AssignedTo;
        entity.IsVisibleToResidents = model.IsVisibleToResidents;
        entity.CreatedAt = DateTime.SpecifyKind(model.CreatedAt, DateTimeKind.Utc);
        entity.DueDate = model.DueDate.HasValue ? DateTime.SpecifyKind(model.DueDate.Value, DateTimeKind.Utc) : null;
        entity.ResolvedAt = model.Status is ServiceRequestStatus.Resolved or ServiceRequestStatus.Closed
            ? (entity.ResolvedAt ?? DateTime.UtcNow)
            : null;

        await db.SaveChangesAsync();
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

    private async Task FillOptionsAsync()
    {
        ViewBag.UnitOptions = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .Where(x => x.Active)
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .Select(x => new SelectListItem(UnitDisplayHelper.Display(x), x.Id.ToString()))
            .ToListAsync();
    }
}
