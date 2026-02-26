using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize]
public class LedgerController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var rows = await db.LedgerTransactions.AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .ToListAsync();
        return View(rows);
    }

    public async Task<IActionResult> Create()
    {
        return View(await BuildAsync(new LedgerTransactionCreateViewModel()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LedgerTransactionCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(await BuildAsync(model));
        }

        db.LedgerTransactions.Add(new LedgerTransaction
        {
            Date = DateTimeHelper.EnsureUtc(model.Date),
            IncomeExpenseCategoryId = model.IncomeExpenseCategoryId,
            Amount = model.Amount,
            PaymentChannel = model.PaymentChannel,
            Description = model.Description
        });

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var entity = await db.LedgerTransactions.FindAsync(id);
        if (entity is null)
        {
            return NotFound();
        }

        var model = new LedgerTransactionCreateViewModel
        {
            Date = entity.Date,
            IncomeExpenseCategoryId = entity.IncomeExpenseCategoryId,
            Amount = entity.Amount,
            PaymentChannel = entity.PaymentChannel,
            Description = entity.Description
        };

        ViewBag.TransactionId = id;
        return View(await BuildAsync(model));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, LedgerTransactionCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.TransactionId = id;
            return View(await BuildAsync(model));
        }

        var entity = await db.LedgerTransactions.FindAsync(id);
        if (entity is null)
        {
            return NotFound();
        }

        entity.Date = DateTimeHelper.EnsureUtc(model.Date);
        entity.IncomeExpenseCategoryId = model.IncomeExpenseCategoryId;
        entity.Amount = model.Amount;
        entity.PaymentChannel = model.PaymentChannel;
        entity.Description = model.Description;

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task<LedgerTransactionCreateViewModel> BuildAsync(LedgerTransactionCreateViewModel model)
    {
        model.CategoryOptions = await db.IncomeExpenseCategories
            .AsNoTracking()
            .Where(x => x.Active)
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .Select(x => new SelectListItem($"{CategoryTypeHelper.Display(x.Type)} - {x.Name}", x.Id.ToString()))
            .ToListAsync();

        return model;
    }
}
