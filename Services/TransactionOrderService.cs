using System.Data;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kumburgaz.Web.Services;

public sealed record TxRef(string Source, int Id);

public class TransactionOrderService(ApplicationDbContext db)
{
    // PostgreSQL zaman damgaları mikrosaniye duyarlılığındadır.
    public const long TickStep = 10;

    internal const string InvalidOrderMessage = "Sıralama listesi bu günün işlemleriyle uyuşmuyor; sayfayı yenileyin.";

    public async Task ReorderDayAsync(string accountKey, DateOnly day, IReadOnlyList<TxRef> oldestFirst)
    {
        try
        {
            // Satır kümesi, aktarım eşleri, saatler ve denetim kayıtları aynı işlemde kalır.
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                : null;
            var account = await RequireAccountAsync(accountKey);
            var start = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var end = start.AddTicks(TimeSpan.TicksPerDay - 1);
            var (accountCollections, accountLedger) = AccountRows(account);
            var collections = await accountCollections.Where(x => x.Date >= start && x.Date <= end)
                .ToDictionaryAsync(x => x.Id);
            var ledger = await accountLedger.Where(x => x.Date >= start && x.Date <= end)
                .Include(x => x.IncomeExpenseCategory).ToDictionaryAsync(x => x.Id);

            var expected = collections.Keys.Select(id => new TxRef("collection", id))
                .Concat(ledger.Keys.Select(id => new TxRef("ledger", id))).ToHashSet();
            if (oldestFirst.Count != expected.Count || !expected.SetEquals(oldestFirst))
            {
                throw new InvalidOperationException(InvalidOrderMessage);
            }

            var slots = oldestFirst.Select((row, index) => (Row: row, Date: ApplySlot(start, index + 1L))).ToList();
            var movingLedger = slots
                .Where(slot => slot.Row.Source == "ledger" && ledger[slot.Row.Id].Date != slot.Date)
                .Select(slot => ledger[slot.Row.Id]);
            await TransferPairing.PreserveLegacyPairsForReorderAsync(db, movingLedger);

            foreach (var (row, date) in slots)
            {
                if (row.Source == "collection")
                {
                    collections[row.Id].Date = date;
                }
                else
                {
                    ledger[row.Id].Date = date;
                }
            }

            await db.SaveChangesAsync();
            if (transaction is not null)
            {
                await transaction.CommitAsync();
            }
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new InvalidOperationException(InvalidOrderMessage, ex);
        }
        catch (Exception ex) when ((ex is DbUpdateException ? ex.InnerException : ex) is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        {
            throw new InvalidOperationException(InvalidOrderMessage, ex);
        }
    }

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

    private static DateTime ApplySlot(DateTime date, long slot)
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
        var (collections, ledger) = AccountRows(account);
        return collections.AsNoTracking().Select(x => x.Date).Concat(ledger.AsNoTracking().Select(x => x.Date));
    }

    private (IQueryable<Collection> Collections, IQueryable<LedgerTransaction> Ledger) AccountRows(
        ActiveFinancialAccount account)
    {
        if (account.CashBoxId.HasValue)
        {
            return (db.Collections.Where(x => x.CashBoxId == account.CashBoxId),
                db.LedgerTransactions.Where(x => x.CashBoxId == account.CashBoxId));
        }

        return (db.Collections.Where(x => x.BankAccountId == account.BankAccountId),
            db.LedgerTransactions.Where(x => x.BankAccountId == account.BankAccountId));
    }

    private async Task<ActiveFinancialAccount> RequireAccountAsync(string accountKey) =>
        await FinancialAccountHelper.FindAsync(db, accountKey, activeOnly: false)
        ?? throw new InvalidOperationException("Geçerli bir kasa/banka hesabı seçiniz.");

    private static long GetSlot(DateTime date) => date.TimeOfDay.Ticks / TickStep;
}
