using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize(Policy = AppPolicies.ManagementWrite)]
public class BlocksController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var blocks = await db.Blocks.AsNoTracking()
            .Include(x => x.Units.Where(u => u.Active))
            .OrderBy(x => x.Name)
            .ToListAsync();
        return View(blocks);
    }

    public async Task<IActionResult> Create()
    {
        var site = await db.Sites.FirstOrDefaultAsync();
        if (site is null)
        {
            TempData["ActionError"] = "Önce bir site oluşturulmalıdır.";
            return RedirectToAction(nameof(Index));
        }
        return View(new Block { SiteId = site.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Block model)
    {
        ModelState.Remove(nameof(Block.Site));
        ModelState.Remove(nameof(Block.Units));

        if (!ModelState.IsValid)
            return View(model);

        var exists = await db.Blocks.AnyAsync(x => x.Name == model.Name.Trim() && x.SiteId == model.SiteId);
        if (exists)
        {
            ModelState.AddModelError(nameof(Block.Name), "Bu isimde bir blok zaten var.");
            return View(model);
        }

        model.Name = model.Name.Trim();
        db.Blocks.Add(model);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = $"'{model.Name}' bloğu oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var entity = await db.Blocks.FindAsync(id);
        if (entity is null) return NotFound();
        return View(entity);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Block model)
    {
        ModelState.Remove(nameof(Block.Site));
        ModelState.Remove(nameof(Block.Units));

        if (!ModelState.IsValid)
            return View(model);

        var exists = await db.Blocks.AnyAsync(x => x.Name == model.Name.Trim() && x.SiteId == model.SiteId && x.Id != model.Id);
        if (exists)
        {
            ModelState.AddModelError(nameof(Block.Name), "Bu isimde bir blok zaten var.");
            return View(model);
        }

        var entity = await db.Blocks.FindAsync(model.Id);
        if (entity is null) return NotFound();

        entity.Name = model.Name.Trim();
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = $"'{entity.Name}' bloğu güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await db.Blocks.FindAsync(id);
        if (entity is null)
        {
            TempData["ActionError"] = "Blok bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        var hasUnits = await db.Units.AnyAsync(x => x.BlockId == id);
        if (hasUnits)
        {
            TempData["ActionError"] = "Bu blokta daireler var. Önce daireleri silin.";
            return RedirectToAction(nameof(Index));
        }

        db.Blocks.Remove(entity);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = $"'{entity.Name}' bloğu silindi.";
        return RedirectToAction(nameof(Index));
    }
}
