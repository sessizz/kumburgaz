using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
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
            Date = model.Date,
            IncomeExpenseCategoryId = model.IncomeExpenseCategoryId,
            Amount = model.Amount,
            PaymentChannel = model.PaymentChannel,
            Description = model.Description
        });

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
            .Select(x => new SelectListItem($"{x.Type} - {x.Name}", x.Id.ToString()))
            .ToListAsync();

        return model;
    }
}
