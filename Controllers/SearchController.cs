using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[ModuleAuthorize(AppModules.Raporlar)]
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
        var hasNumericId = int.TryParse(query, out var numericId);
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
            .Include(x => x.BillingGroup).ThenInclude(x => x!.DuesType)
            .Include(x => x.BankAccount)
            .Include(x => x.CashBox)
            .Where(x => (hasNumericId && x.Id == numericId)
                        || (x.ReferenceNo != null && x.ReferenceNo.ToLower().Contains(normalized))
                        || (x.Note != null && x.Note.ToLower().Contains(normalized))
                        || (x.Unit != null && x.Unit.UnitNo.ToLower().Contains(normalized))
                        || (x.Unit != null && x.Unit.OwnerName != null && x.Unit.OwnerName.ToLower().Contains(normalized))
                        || (x.Unit != null && x.Unit.Block != null && x.Unit.Block.Name.ToLower().Contains(normalized))
                        || (x.BillingGroup != null && x.BillingGroup.Name.ToLower().Contains(normalized))
                        || (x.BillingGroup != null && x.BillingGroup.DuesType != null && x.BillingGroup.DuesType.Name.ToLower().Contains(normalized))
                        || (x.BankAccount != null && x.BankAccount.Name.ToLower().Contains(normalized))
                        || (x.CashBox != null && x.CashBox.Name.ToLower().Contains(normalized)))
            .OrderByDescending(x => x.Date)
            .Take(8)
            .ToListAsync();

        results.AddRange(collections.Select(x => new
        {
            label = $"Tahsilat #{x.Id} • {FormatMoney(x.Amount)} TL",
            subtitle = $"{x.Date:dd.MM.yyyy} • {UnitDisplayHelper.Display(x.Unit)} • {x.Unit?.OwnerName ?? "-"} • {(x.ReferenceNo is null ? "Makbuz yok" : $"Makbuz: {x.ReferenceNo}")}",
            active = true,
            url = Url.Action("Edit", "Collections", new { id = x.Id, returnUrl = Url.Action("Detail", "Units", new { id = x.UnitId }) })
        }));

        var ledgerRows = await db.LedgerTransactions.AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Include(x => x.BankAccount)
            .Include(x => x.CashBox)
            .Where(x => (hasNumericId && x.Id == numericId)
                        || (x.Description != null && x.Description.ToLower().Contains(normalized))
                        || (x.IncomeExpenseCategory != null && x.IncomeExpenseCategory.Name.ToLower().Contains(normalized))
                        || (x.BankAccount != null && x.BankAccount.Name.ToLower().Contains(normalized))
                        || (x.BankAccount != null && x.BankAccount.Branch != null && x.BankAccount.Branch.ToLower().Contains(normalized))
                        || (x.BankAccount != null && x.BankAccount.Iban != null && x.BankAccount.Iban.ToLower().Contains(normalized))
                        || (x.CashBox != null && x.CashBox.Name.ToLower().Contains(normalized)))
            .OrderByDescending(x => x.Date)
            .Take(8)
            .ToListAsync();

        results.AddRange(ledgerRows.Select(x => new
        {
            label = $"Finans #{x.Id} • {x.Description ?? x.IncomeExpenseCategory?.Name ?? "Hareket"}",
            subtitle = $"{LedgerTypeLabel(x)} • {x.Date:dd.MM.yyyy} • {FormatMoney(x.Amount)} TL • {x.BankAccount?.Name ?? x.CashBox?.Name ?? "-"}",
            active = true,
            url = x.BankAccountId.HasValue
                ? Url.Action("BankDetail", "CashBank", new { id = x.BankAccountId.Value })
                : x.CashBoxId.HasValue
                    ? Url.Action("CashBoxDetail", "CashBank", new { id = x.CashBoxId.Value })
                    : Url.Action("Index", "Ledger")
        }));

        var bankAccounts = await db.BankAccounts.AsNoTracking()
            .Where(x => (hasNumericId && x.Id == numericId)
                        || x.Name.ToLower().Contains(normalized)
                        || (x.Branch != null && x.Branch.ToLower().Contains(normalized))
                        || (x.Iban != null && x.Iban.ToLower().Contains(normalized)))
            .OrderBy(x => x.Name)
            .Take(5)
            .ToListAsync();

        results.AddRange(bankAccounts.Select(x => new
        {
            label = x.Name,
            subtitle = $"Banka • {x.Branch ?? "-"} • {x.Iban ?? "IBAN yok"}",
            active = x.Active,
            url = Url.Action("BankDetail", "CashBank", new { id = x.Id })
        }));

        var cashBoxes = await db.CashBoxes.AsNoTracking()
            .Where(x => (hasNumericId && x.Id == numericId)
                        || x.Name.ToLower().Contains(normalized))
            .OrderBy(x => x.Name)
            .Take(5)
            .ToListAsync();

        results.AddRange(cashBoxes.Select(x => new
        {
            label = x.Name,
            subtitle = "Kasa",
            active = x.Active,
            url = Url.Action("CashBoxDetail", "CashBank", new { id = x.Id })
        }));

        var categories = await db.IncomeExpenseCategories.AsNoTracking()
            .Where(x => (hasNumericId && x.Id == numericId)
                        || x.Name.ToLower().Contains(normalized)
                        || x.Type.ToLower().Contains(normalized))
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .Take(6)
            .ToListAsync();

        results.AddRange(categories.Select(x => new
        {
            label = x.Name,
            subtitle = $"Kategori • {x.Type}",
            active = x.Active,
            url = Url.Action("Index", "IncomeExpenseCategories")
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

    private static string FormatMoney(decimal amount) =>
        amount.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));

    private static string LedgerTypeLabel(LedgerTransaction transaction)
    {
        if (transaction.IsTransfer)
        {
            return "Transfer";
        }

        return transaction.IncomeExpenseCategory?.Type ?? "Finans";
    }
}
