using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

/// <summary>
/// Kasa/banka bakiye listesi. Masaustu CashBankController.Index ile mobil KasaBankaController
/// AYNI kaynagi kullansin diye buraya cikarildi (rakamlarin birebir eslesmesi icin).
/// </summary>
public static class CashBankBalanceHelper
{
    public static async Task<List<CashBankListItemViewModel>> BuildAsync(ApplicationDbContext db)
    {
        var collections = await db.Collections
            .AsNoTracking()
            .Select(x => new { x.CashBoxId, x.BankAccountId, x.Amount })
            .ToListAsync();

        var ledgerRows = await db.LedgerTransactions
            .AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Select(x => new
            {
                x.CashBoxId,
                x.BankAccountId,
                x.Amount,
                x.IsTransfer,
                x.TransferIsIncoming,
                Type = x.IncomeExpenseCategory != null ? x.IncomeExpenseCategory.Type : CategoryTypeHelper.Gider
            })
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
            Id = x.Id,
            Type = "cash",
            Name = x.Name,
            Balance = x.OpeningBalance
                + collections.Where(c => c.CashBoxId == x.Id).Sum(c => c.Amount)
                + ledgerRows.Where(e => e.CashBoxId == x.Id).Sum(e => e.IsTransfer
                    ? (e.TransferIsIncoming ? e.Amount : -e.Amount)
                    : (e.Type == CategoryTypeHelper.Gelir ? e.Amount : -e.Amount))
        }));
        items.AddRange(bankAccounts.Select(x => new CashBankListItemViewModel
        {
            Id = x.Id,
            Type = "bank",
            Name = string.IsNullOrWhiteSpace(x.Branch) ? x.Name : $"{x.Name} - {x.Branch}",
            Detail = x.Iban,
            Balance = x.OpeningBalance
                + collections.Where(c => c.BankAccountId == x.Id).Sum(c => c.Amount)
                + ledgerRows.Where(e => e.BankAccountId == x.Id).Sum(e => e.IsTransfer
                    ? (e.TransferIsIncoming ? e.Amount : -e.Amount)
                    : (e.Type == CategoryTypeHelper.Gelir ? e.Amount : -e.Amount))
        }));

        return items.OrderByDescending(x => x.Type == "bank").ThenBy(x => x.Name).ToList();
    }
}
