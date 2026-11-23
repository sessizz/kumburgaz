using System.Data;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
            var (start, end) = DayBounds(day);
            await using var transaction = await BeginWriteAsync((accountKey, start));
            var account = await RequireAccountAsync(accountKey);
            var (accountCollections, accountLedger) = AccountRows(account, CashBankDetailService.VisibleCollections(db));
            accountCollections = accountCollections.Where(x => x.Date >= start);
            accountLedger = accountLedger.Where(x => x.Date >= start);
            if (end is DateTime nextDay)
            {
                accountCollections = accountCollections.Where(x => x.Date < nextDay);
                accountLedger = accountLedger.Where(x => x.Date < nextDay);
            }

            var collections = await accountCollections.ToDictionaryAsync(x => x.Id);
            var ledger = await accountLedger.Include(x => x.IncomeExpenseCategory).ToDictionaryAsync(x => x.Id);

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

    // Sıra okuması ve kayıt aynı işlemde kalır. Çağıranın işlemi varsa kilit onun sonuna kadar tutulur.
    internal async Task<IDbContextTransaction?> BeginWriteAsync(params (string? AccountKey, DateTime Day)[] locations)
    {
        if (!db.Database.IsRelational())
        {
            return null;
        }

        var postgres = db.Database.IsNpgsql();
        var current = db.Database.CurrentTransaction;
        // PostgreSQL'de kilit bekleyen sorgudan sonraki SELECT güncel işlem anlık görüntüsünü alır.
        var owned = current is not null ? null : postgres
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted)
            : await db.Database.BeginTransactionAsync();
        try
        {
            if (postgres)
            {
                if (current is not null && current.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
                {
                    throw new InvalidOperationException(InvalidOrderMessage);
                }

                var keys = new SortedSet<long>();
                foreach (var (accountKey, day) in locations)
                {
                    if (FinancialAccountHelper.TryParse(accountKey, out _, out var cashId, out var bankId))
                    {
                        var account = cashId ?? -(long)bankId!.Value;
                        // 0x4B öneki bu kilitleri ayırır; gün ve işaretli hesap kimliği çakışmaz.
                        keys.Add(0x4B00000000000000L | ((long)DateOnly.FromDateTime(day).DayNumber << 32)
                            | (account & uint.MaxValue));
                    }
                }

                foreach (var key in keys)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})");
                }
            }

            return owned;
        }
        catch
        {
            if (owned is not null)
            {
                await owned.DisposeAsync();
            }

            throw;
        }
    }

    internal static (string? AccountKey, DateTime Day) Location(int? cashId, int? bankId, DateTime date) =>
        (FinancialAccountHelper.BuildKey(cashId, bankId), date.Date);

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
        var (day, end) = DayBounds(DateOnly.FromDateTime(date));
        var dates = AccountDates(account).Where(value => value >= day);
        if (end is DateTime nextDay)
        {
            dates = dates.Where(value => value < nextDay);
        }

        var latest = await dates.OrderByDescending(value => value).FirstOrDefaultAsync();
        return ApplySlot(day, GetSlot(latest) + 1);
    }

    private static (DateTime Start, DateTime? End) DayBounds(DateOnly day)
    {
        var start = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return (start, day == DateOnly.MaxValue ? null : start.AddDays(1));
    }

    private IQueryable<DateTime> AccountDates(ActiveFinancialAccount account)
    {
        var (collections, ledger) = AccountRows(account, db.Collections);
        return collections.AsNoTracking().Select(x => x.Date).Concat(ledger.AsNoTracking().Select(x => x.Date));
    }

    private (IQueryable<Collection> Collections, IQueryable<LedgerTransaction> Ledger) AccountRows(
        ActiveFinancialAccount account, IQueryable<Collection> collections)
    {
        if (account.CashBoxId.HasValue)
        {
            return (collections.Where(x => x.CashBoxId == account.CashBoxId),
                db.LedgerTransactions.Where(x => x.CashBoxId == account.CashBoxId));
        }

        return (collections.Where(x => x.BankAccountId == account.BankAccountId),
            db.LedgerTransactions.Where(x => x.BankAccountId == account.BankAccountId));
    }

    private async Task<ActiveFinancialAccount> RequireAccountAsync(string accountKey) =>
        await FinancialAccountHelper.FindAsync(db, accountKey, activeOnly: false)
        ?? throw new InvalidOperationException("Geçerli bir kasa/banka hesabı seçiniz.");

    private static long GetSlot(DateTime date) => date.TimeOfDay.Ticks / TickStep;
}
