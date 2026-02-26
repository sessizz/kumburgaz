using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize]
public class IncomeExpenseCategoriesController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var rows = await db.IncomeExpenseCategories.AsNoTracking()
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .ToListAsync();

        return View(rows);
    }

    public IActionResult Create()
    {
        PopulateTypes();
        return View(new IncomeExpenseCategory { Active = true, Type = CategoryTypeHelper.Gider });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(IncomeExpenseCategory model)
    {
        model.Type = CategoryTypeHelper.Normalize(model.Type);
        if (!ModelState.IsValid)
        {
            PopulateTypes();
            return View(model);
        }

        db.IncomeExpenseCategories.Add(model);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var entity = await db.IncomeExpenseCategories.FindAsync(id);
        if (entity is null)
        {
            return NotFound();
        }

        entity.Type = CategoryTypeHelper.Normalize(entity.Type);
        PopulateTypes();
        return View(entity);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(IncomeExpenseCategory model)
    {
        model.Type = CategoryTypeHelper.Normalize(model.Type);
        if (!ModelState.IsValid)
        {
            PopulateTypes();
            return View(model);
        }

        db.IncomeExpenseCategories.Update(model);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var entity = await db.IncomeExpenseCategories.FindAsync(id);
        if (entity is null)
        {
            return NotFound();
        }

        entity.Active = !entity.Active;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private void PopulateTypes()
    {
        ViewBag.Types = new List<SelectListItem>
        {
            new(CategoryTypeHelper.Gelir, CategoryTypeHelper.Gelir),
            new(CategoryTypeHelper.Gider, CategoryTypeHelper.Gider)
        };
    }
}
