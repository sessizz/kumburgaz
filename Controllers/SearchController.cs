using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize(Policy = AppPolicies.ReportsRead)]
public class SearchController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Global(string? term)
    {
        var query = term?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return Json(Array.Empty<object>());
        }

        var normalized = query.ToLowerInvariant();
        var results = new List<object>();

        var units = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .Where(x => x.UnitNo.ToLower().Contains(normalized)
                        || (x.OwnerName != null && x.OwnerName.ToLower().Contains(normalized))
                        || (x.Block != null && x.Block.Name.ToLower().Contains(normalized)))
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .Take(8)
            .ToListAsync();

        results.AddRange(units.Select(x => new
        {
            label = UnitDisplayHelper.Display(x),
            subtitle = $"Daire • {x.OwnerName}",
            active = x.Active,
            url = Url.Action("Detail", "Units", new { id = x.Id })
        }));

        var accounts = await db.Accounts.AsNoTracking()
            .Where(x => x.Name.ToLower().Contains(normalized)
                        || (x.Phone != null && x.Phone.ToLower().Contains(normalized))
                        || (x.Email != null && x.Email.ToLower().Contains(normalized)))
            .OrderBy(x => x.Name)
            .Take(8)
            .ToListAsync();

        results.AddRange(accounts.Select(x => new
        {
            label = x.Name,
            subtitle = $"Hesap • {AccountDisplayHelper.TypeLabel(x.AccountType)}",
            active = x.Active,
            url = Url.Action("Detail", "Accounts", new { id = x.Id })
        }));

        var collections = await db.Collections.AsNoTracking()
            .Include(x => x.Unit).ThenInclude(x => x!.Block)
            .Where(x => (x.ReferenceNo != null && x.ReferenceNo.ToLower().Contains(normalized))
                        || (x.Note != null && x.Note.ToLower().Contains(normalized)))
            .OrderByDescending(x => x.Date)
            .Take(8)
            .ToListAsync();

        results.AddRange(collections.Select(x => new
        {
            label = x.ReferenceNo ?? x.Note ?? "Tahsilat",
            subtitle = $"Tahsilat • {x.Date:dd.MM.yyyy} • {x.Amount:N2} TL • {UnitDisplayHelper.Display(x.Unit)}",
            active = true,
            url = Url.Action("Detail", "Units", new { id = x.UnitId })
        }));

        var ledgerRows = await db.LedgerTransactions.AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Where(x => (x.Description != null && x.Description.ToLower().Contains(normalized))
                        || (x.IncomeExpenseCategory != null && x.IncomeExpenseCategory.Name.ToLower().Contains(normalized)))
            .OrderByDescending(x => x.Date)
            .Take(8)
            .ToListAsync();

        results.AddRange(ledgerRows.Select(x => new
        {
            label = x.Description ?? x.IncomeExpenseCategory?.Name ?? "Finans hareketi",
            subtitle = $"{(x.IncomeExpenseCategory?.Type ?? "Transfer")} • {x.Date:dd.MM.yyyy} • {x.Amount:N2} TL",
            active = true,
            url = x.BankAccountId.HasValue
                ? Url.Action("BankDetail", "CashBank", new { id = x.BankAccountId.Value })
                : x.CashBoxId.HasValue
                    ? Url.Action("CashBoxDetail", "CashBank", new { id = x.CashBoxId.Value })
                    : Url.Action("Index", "Ledger")
        }));

        var documents = await db.DocumentRecords.AsNoTracking()
            .Where(x => x.Title.ToLower().Contains(normalized)
                        || x.Category.ToLower().Contains(normalized)
                        || (x.Note != null && x.Note.ToLower().Contains(normalized)))
            .OrderByDescending(x => x.DocumentDate)
            .Take(5)
            .ToListAsync();

        results.AddRange(documents.Select(x => new
        {
            label = x.Title,
            subtitle = $"Belge • {x.Category}",
            active = true,
            url = Url.Action("Index", "Documents")
        }));

        var requests = await db.ServiceRequests.AsNoTracking()
            .Where(x => x.Title.ToLower().Contains(normalized)
                        || (x.Description != null && x.Description.ToLower().Contains(normalized)))
            .OrderByDescending(x => x.CreatedAt)
            .Take(5)
            .ToListAsync();

        results.AddRange(requests.Select(x => new
        {
            label = x.Title,
            subtitle = $"Talep • {EnumDisplayHelper.Display(x.Status)}",
            active = true,
            url = Url.Action("Index", "Requests")
        }));

        return Json(results.Take(20));
    }
}
