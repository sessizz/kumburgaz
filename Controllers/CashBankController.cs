using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize]
public class CashBankController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(string? q = null)
    {
        var query = q?.Trim();

        var collections = await db.Collections
            .AsNoTracking()
            .Select(x => new { x.CashBoxId, x.BankAccountId, x.Amount })
            .ToListAsync();

        var expenses = await db.LedgerTransactions
            .AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Where(x => x.IncomeExpenseCategory != null && x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gider)
            .Select(x => new { x.CashBoxId, x.BankAccountId, x.Amount })
            .ToListAsync();

        var cashBoxes = await db.CashBoxes
            .AsNoTracking()
            .Where(x => x.Active)
            .OrderBy(x => x.Name)
            .ToListAsync();

        var bankAccounts = await db.BankAccounts
            .AsNoTracking()
            .Where(x => x.Active)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Branch)
            .ToListAsync();

        var items = new List<CashBankListItemViewModel>();
        items.AddRange(cashBoxes.Select(x => new CashBankListItemViewModel
        {
            Type = "cash",
            Name = x.Name,
            Balance = x.OpeningBalance
                + collections.Where(c => c.CashBoxId == x.Id).Sum(c => c.Amount)
                - expenses.Where(e => e.CashBoxId == x.Id).Sum(e => e.Amount)
        }));
        items.AddRange(bankAccounts.Select(x => new CashBankListItemViewModel
        {
            Type = "bank",
            Name = string.IsNullOrWhiteSpace(x.Branch) ? x.Name : $"{x.Name} - {x.Branch}",
            Detail = x.Iban,
            Balance = x.OpeningBalance
                + collections.Where(c => c.BankAccountId == x.Id).Sum(c => c.Amount)
                - expenses.Where(e => e.BankAccountId == x.Id).Sum(e => e.Amount)
        }));

        if (!string.IsNullOrWhiteSpace(query))
        {
            items = items
                .Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || (x.Detail?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        return View(new CashBankIndexViewModel
        {
            Items = items.OrderByDescending(x => x.Type == "bank").ThenBy(x => x.Name).ToList(),
            Query = query ?? string.Empty
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCashBox(CashBoxFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ActionError"] = "Kasa bilgilerini kontrol edin.";
            return RedirectToAction(nameof(Index));
        }

        db.CashBoxes.Add(new CashBox
        {
            Name = model.Name,
            OpeningBalance = model.OpeningBalance,
            OpeningBalanceDate = DateTimeHelper.EnsureUtc(model.OpeningBalanceDate),
            Active = true
        });
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Kasa eklendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBankAccount(BankAccountFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ActionError"] = "Banka bilgilerini kontrol edin.";
            return RedirectToAction(nameof(Index));
        }

        db.BankAccounts.Add(new BankAccount
        {
            Name = model.Name,
            Branch = model.Branch,
            Iban = model.Iban,
            OpeningBalance = model.OpeningBalance,
            OpeningBalanceDate = DateTimeHelper.EnsureUtc(model.OpeningBalanceDate),
            Active = true
        });
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Banka kartı eklendi.";
        return RedirectToAction(nameof(Index));
    }
}
