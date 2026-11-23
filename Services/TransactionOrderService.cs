using Kumburgaz.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public class TransactionOrderService(ApplicationDbContext db)
{
    // PostgreSQL zaman damgaları mikrosaniye duyarlılığındadır.
    public const long TickStep = 10;

    public async Task<DateTime> NextSlotAsync(string accountKey, DateTime date)
    {
        if (!FinancialAccountHelper.TryParse(accountKey, out _, out var cashBoxId, out var bankAccountId))
        {
            throw new InvalidOperationException("Geçerli bir kasa/banka hesabı seçiniz.");
        }

        var day = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
        var dayEnd = day.AddTicks(TimeSpan.TicksPerDay - 1);
        var collections = db.Collections.AsNoTracking().Where(x => x.Date >= day && x.Date <= dayEnd);
        var ledger = db.LedgerTransactions.AsNoTracking().Where(x => x.Date >= day && x.Date <= dayEnd);

        if (cashBoxId.HasValue)
        {
            collections = collections.Where(x => x.CashBoxId == cashBoxId);
            ledger = ledger.Where(x => x.CashBoxId == cashBoxId);
        }
        else
        {
            collections = collections.Where(x => x.BankAccountId == bankAccountId);
            ledger = ledger.Where(x => x.BankAccountId == bankAccountId);
        }

        var latest = await collections.Select(x => (DateTime?)x.Date)
            .Concat(ledger.Select(x => (DateTime?)x.Date))
            .OrderByDescending(x => x)
            .FirstOrDefaultAsync();
        var currentSlot = latest.GetValueOrDefault().TimeOfDay.Ticks / TickStep;
        var nextOffset = (currentSlot + 1) * TickStep;
        if (nextOffset >= TimeSpan.TicksPerDay)
        {
            throw new InvalidOperationException("Bu gün için yeni işlem sırası ayrılamadı.");
        }

        return day.AddTicks(nextOffset);
    }

    public Task<DateTime> ResolveEditedTransactionDateAsync(
        string accountKey, DateTime submittedDate, DateTime existingDate) =>
        submittedDate.Date == existingDate.Date
            ? Task.FromResult(existingDate)
            : NextSlotAsync(accountKey, submittedDate);
}
