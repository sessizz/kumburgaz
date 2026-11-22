using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public sealed record ActiveFinancialAccount(string Key, string Name, PaymentChannel Channel, int? CashBoxId, int? BankAccountId);

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

    /// <summary>Anahtarı aktif bir kasa/banka hesabına çözer; anahtar bozuksa ya da hesap yok/pasifse null.</summary>
    public static async Task<ActiveFinancialAccount?> FindActiveAsync(ApplicationDbContext db, string? key)
    {
        if (!TryParse(key, out var channel, out var cashBoxId, out var bankAccountId))
        {
            return null;
        }

        if (cashBoxId.HasValue)
        {
            var cashBox = await db.CashBoxes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == cashBoxId.Value && x.Active);
            return cashBox is null
                ? null
                : new ActiveFinancialAccount(CashKey(cashBox.Id), cashBox.Name, channel, cashBox.Id, null);
        }

        var bank = await db.BankAccounts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == bankAccountId!.Value && x.Active);
        if (bank is null)
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(bank.Branch) ? bank.Name : $"{bank.Name} - {bank.Branch}";
        return new ActiveFinancialAccount(BankKey(bank.Id), name, channel, null, bank.Id);
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
