using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize]
public class DuesTypesController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        return View(await db.DuesTypes.AsNoTracking().OrderBy(x => x.Name).ToListAsync());
    }

    public IActionResult Create() => View(new DuesType { Active = true });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DuesType model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        db.DuesTypes.Add(model);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var entity = await db.DuesTypes.FindAsync(id);
        if (entity is null)
        {
            return NotFound();
        }

        return View(entity);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(DuesType model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        db.DuesTypes.Update(model);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
