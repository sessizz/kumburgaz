using Kumburgaz.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public class TransactionOrderService(ApplicationDbContext db)
{
    // PostgreSQL zaman damgaları mikrosaniye duyarlılığındadır.
    public const long TickStep = 10;

    public async Task<DateTime> NextSlotAsync(string accountKey, DateTime date)
    {
        var account = await RequireAccountAsync(accountKey);
        return await NextSlotAsync(account, date);
    }

    public async Task<DateTime> ResolveEditedTransactionDateAsync(
        string accountKey, DateTime submittedDate, DateTime existingDate)
    {
        var account = await RequireAccountAsync(accountKey);
        return submittedDate.Date == existingDate.Date
            ? existingDate
            : await NextSlotAsync(account, submittedDate);
    }

    public async Task<Dictionary<DateTime, long>> BuildExistingTransactionDateOffsetsAsync(string accountKey)
    {
        var account = await RequireAccountAsync(accountKey);
        var dates = await AccountDates(account).ToListAsync();
        return dates.GroupBy(date => date.Date).ToDictionary(group => group.Key, group => group.Max(GetSlot));
    }

    internal static DateTime ApplySlot(DateTime date, long slot)
    {
        var offset = slot * TickStep;
        if (offset >= TimeSpan.TicksPerDay)
        {
            throw new InvalidOperationException("Bu gün için yeni işlem sırası ayrılamadı.");
        }

        return DateTime.SpecifyKind(date.Date, DateTimeKind.Utc).AddTicks(offset);
    }

    private async Task<DateTime> NextSlotAsync(ActiveFinancialAccount account, DateTime date)
    {
        var day = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        var dayEnd = day.AddTicks(TimeSpan.TicksPerDay - 1);
        var latest = await AccountDates(account)
            .Where(value => value >= day && value <= dayEnd)
            .OrderByDescending(value => value)
            .FirstOrDefaultAsync();
        return ApplySlot(day, GetSlot(latest) + 1);
    }

    private IQueryable<DateTime> AccountDates(ActiveFinancialAccount account)
    {
        var collections = db.Collections.AsNoTracking();
        var ledger = db.LedgerTransactions.AsNoTracking();
        if (account.CashBoxId.HasValue)
        {
            collections = collections.Where(x => x.CashBoxId == account.CashBoxId);
            ledger = ledger.Where(x => x.CashBoxId == account.CashBoxId);
        }
        else
        {
            collections = collections.Where(x => x.BankAccountId == account.BankAccountId);
            ledger = ledger.Where(x => x.BankAccountId == account.BankAccountId);
        }

        return collections.Select(x => x.Date).Concat(ledger.Select(x => x.Date));
    }

    private async Task<ActiveFinancialAccount> RequireAccountAsync(string accountKey) =>
        await FinancialAccountHelper.FindAsync(db, accountKey, activeOnly: false)
        ?? throw new InvalidOperationException("Geçerli bir kasa/banka hesabı seçiniz.");

    private static long GetSlot(DateTime date) => date.TimeOfDay.Ticks / TickStep;
}
