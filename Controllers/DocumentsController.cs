using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[ModuleAuthorize(AppModules.Belgeler)]
public class DocumentsController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var rows = await db.DocumentRecords.AsNoTracking()
            .OrderByDescending(x => x.DocumentDate)
            .ThenBy(x => x.Category)
            .ToListAsync();
        return View(rows);
    }

    public IActionResult Create() => View(new DocumentRecord { DocumentDate = DateTime.Today });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DocumentRecord model)
    {
        if (!ModelState.IsValid) return View(model);
        model.DocumentDate = DateTime.SpecifyKind(model.DocumentDate, DateTimeKind.Utc);
        db.DocumentRecords.Add(model);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Belge kaydı oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var entity = await db.DocumentRecords.FindAsync(id);
        return entity is null ? NotFound() : View(entity);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(DocumentRecord model)
    {
        if (!ModelState.IsValid) return View(model);
        var entity = await db.DocumentRecords.FindAsync(model.Id);
        if (entity is null) return NotFound();

        entity.Title = model.Title.Trim();
        entity.Category = model.Category.Trim();
        entity.Url = model.Url;
        entity.Note = model.Note;
        entity.DocumentDate = DateTime.SpecifyKind(model.DocumentDate, DateTimeKind.Utc);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Belge güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await db.DocumentRecords.FindAsync(id);
        if (entity is null)
        {
            TempData["ActionError"] = "Belge bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        db.DocumentRecords.Remove(entity);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Belge silindi.";
        return RedirectToAction(nameof(Index));
    }
}
