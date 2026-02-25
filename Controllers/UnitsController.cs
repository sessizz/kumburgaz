using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize]
public class UnitsController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var units = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .ToListAsync();

        return View(units);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateBlocksAsync();
        return View(new Unit { Active = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Unit model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateBlocksAsync();
            return View(model);
        }

        db.Units.Add(model);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var unit = await db.Units.FindAsync(id);
        if (unit is null)
        {
            return NotFound();
        }

        await PopulateBlocksAsync();
        return View(unit);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Unit model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateBlocksAsync();
            return View(model);
        }

        db.Units.Update(model);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateBlocksAsync()
    {
        ViewBag.Blocks = await db.Blocks.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString()))
            .ToListAsync();
    }
}
