using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public static class FinancialAccountHelper
{
    public static string CashKey(int id) => $"cash:{id}";
    public static string BankKey(int id) => $"bank:{id}";

    public static bool TryParse(string? key, out PaymentChannel channel, out int? cashBoxId, out int? bankAccountId)
    {
        channel = PaymentChannel.Bank;
        cashBoxId = null;
        bankAccountId = null;

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var parts = key.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var id) || id <= 0)
        {
            return false;
        }

        if (parts[0].Equals("cash", StringComparison.OrdinalIgnoreCase))
        {
            channel = PaymentChannel.Cash;
            cashBoxId = id;
            return true;
        }

        if (parts[0].Equals("bank", StringComparison.OrdinalIgnoreCase))
        {
            channel = PaymentChannel.Bank;
            bankAccountId = id;
            return true;
        }

        return false;
    }

    public static string? BuildKey(int? cashBoxId, int? bankAccountId)
    {
        if (cashBoxId.HasValue)
        {
            return CashKey(cashBoxId.Value);
        }

        return bankAccountId.HasValue ? BankKey(bankAccountId.Value) : null;
    }

    public static async Task<List<SelectListItem>> BuildOptionsAsync(ApplicationDbContext db, string? selectedKey = null)
    {
        var cashBoxes = await db.CashBoxes
            .AsNoTracking()
            .Where(x => x.Active)
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem($"Kasa - {x.Name}", CashKey(x.Id), selectedKey == CashKey(x.Id)))
            .ToListAsync();

        var bankAccounts = await db.BankAccounts
            .AsNoTracking()
            .Where(x => x.Active)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Branch)
            .Select(x => new SelectListItem($"Banka - {x.Name}{(string.IsNullOrWhiteSpace(x.Branch) ? string.Empty : " / " + x.Branch)}", BankKey(x.Id), selectedKey == BankKey(x.Id)))
            .ToListAsync();

        return cashBoxes.Concat(bankAccounts).ToList();
    }
}
