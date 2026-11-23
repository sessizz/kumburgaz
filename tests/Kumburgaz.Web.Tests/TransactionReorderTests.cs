using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class TransactionReorderTests
{
    private static readonly DateTime Day = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Date = DateOnly.FromDateTime(Day);

    [Fact]
    public async Task Reorder_changes_running_balance_order_but_not_the_day()
    {
        await using var db = CreateDb();
        var cash = new CashBox { Name = "Kasa", OpeningBalance = 125m, OpeningBalanceDate = Day.AddDays(-1) };
        var collection = new Models.Collection
        {
            Id = 1, CashBox = cash, Date = Day.AddTicks(10), Amount = 100m,
            Unit = new Unit { Block = new Block { Name = "A" }, UnitNo = "1" },
            BillingGroup = new BillingGroup { Name = "Aidat" }
        };
        var expense = new LedgerTransaction { Id = 1, CashBox = cash, Date = Day.AddTicks(20), Amount = 40m };
        db.AddRange(collection, expense);
        await db.SaveChangesAsync();

        await new TransactionOrderService(db).ReorderDayAsync(FinancialAccountHelper.CashKey(cash.Id), Date,
            [new("ledger", expense.Id), new("collection", collection.Id)]);

        db.ChangeTracker.Clear();
        var detail = await new CashBankDetailService(db, new DuesLedgerRowService(db))
            .BuildAsync("cash", cash.Id, new CashBankDetailQuery());
        Assert.Equal(185m, detail!.Balance);
        var rows = detail.Groups.SelectMany(x => x.Items).Where(x => x.Kind != TxKind.Acilis).ToArray();
        Assert.Collection(rows,
            row => Assert.Equal(("collection", Day.AddTicks(20), 185m), (row.Source, row.Date, row.RunningBalance)),
            row => Assert.Equal(("ledger", Day.AddTicks(10), 85m), (row.Source, row.Date, row.RunningBalance)));
    }

    [Fact]
    public async Task Reorder_rejects_partial_or_foreign_rows_and_changes_nothing()
    {
        await using var db = CreateDb();
        var cash = new CashBox { Name = "Kasa" };
        var bank = new BankAccount { Name = "Banka" };
        var first = new LedgerTransaction { CashBox = cash, Date = Day.AddTicks(80) };
        var second = new LedgerTransaction { CashBox = cash, Date = Day.AddTicks(90) };
        var otherDay = new LedgerTransaction { CashBox = cash, Date = Day.AddDays(1) };
        var otherAccount = new LedgerTransaction { BankAccount = bank, Date = Day.AddTicks(50) };
        var deleted = new LedgerTransaction { CashBox = cash, Date = Day, IsDeleted = true };
        db.AddRange(first, second, otherDay, otherAccount, deleted);
        await db.SaveChangesAsync();
        var expected = await ReadLedgerDatesAsync(db);
        var order = new TransactionOrderService(db);
        var key = FinancialAccountHelper.CashKey(cash.Id);
        TxRef firstRef = new("ledger", first.Id), secondRef = new("ledger", second.Id);

        IReadOnlyList<TxRef>[] invalidOrders =
        [
            [firstRef],
            [firstRef, secondRef, new("ledger", deleted.Id)],
            [firstRef, new("ledger", otherDay.Id)],
            [firstRef, new("ledger", otherAccount.Id)],
            [firstRef, firstRef],
            [firstRef, new("collection", second.Id)],
            [firstRef, new("opening", 0)]
        ];
        foreach (var rows in invalidOrders)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => order.ReorderDayAsync(key, Date, rows));
            Assert.Equal("Sıralama listesi bu günün işlemleriyle uyuşmuyor; sayfayı yenileyin.", error.Message);
            Assert.False(db.ChangeTracker.HasChanges());
            Assert.Equal(expected, await ReadLedgerDatesAsync(db));
        }
    }

    [Theory]
    [InlineData("Bankaya yatır")]
    [InlineData("Bankaya yatır: Eski hareket")]
    public async Task Reorder_preserves_a_unique_description_only_pair_and_its_accounting_direction(string description)
    {
        await using var db = CreateDb();
        var (cash, outgoing, incoming) = await SeedLegacyPairAsync(db, description);
        Assert.Equal(0, await TransferPairing.BackfillAsync(db, NullLogger.Instance));
        var before = await CashBankBalanceHelper.BuildAsync(db);

        await new TransactionOrderService(db).ReorderDayAsync(FinancialAccountHelper.CashKey(cash.Id), Date,
            [new("ledger", outgoing.Id)]);

        db.ChangeTracker.Clear();
        var legs = await db.LedgerTransactions.Include(x => x.IncomeExpenseCategory).OrderBy(x => x.Id).ToListAsync();
        Assert.NotNull(legs[0].TransferGroupId);
        Assert.Equal(legs[0].TransferGroupId, legs[1].TransferGroupId);
        Assert.All(legs, leg => Assert.False(leg.IsTransfer));
        Assert.Equal(Day.AddTicks(10), legs[0].Date);
        Assert.Equal(Day.AddTicks(80), legs[1].Date);
        Assert.Equal(incoming.Id, (await TransferPairing.FindPairAsync(db, legs[0]))?.Id);
        Assert.Equal(outgoing.Id, (await TransferPairing.FindPairAsync(db, legs[1]))?.Id);
        var after = await CashBankBalanceHelper.BuildAsync(db);
        Assert.Equal(before.Select(x => x.Balance), after.Select(x => x.Balance));
        Assert.Equal([-100m, 100m], after.Select(x => x.Balance).Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reorder_rejects_an_ambiguous_legacy_pair(bool extraIncoming)
    {
        await using var db = CreateDb();
        var (cash, outgoing, incoming) = await SeedLegacyPairAsync(db, "Bankaya yatır: Eski hareket");
        db.LedgerTransactions.Add(new LedgerTransaction
        {
            BankAccount = new BankAccount { Name = "Diğer banka" },
            Date = outgoing.Date,
            Amount = outgoing.Amount,
            Description = outgoing.Description,
            IncomeExpenseCategory = extraIncoming ? incoming.IncomeExpenseCategory : outgoing.IncomeExpenseCategory
        });
        await db.SaveChangesAsync();
        var expected = await ReadLedgerDatesAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new TransactionOrderService(db)
            .ReorderDayAsync(FinancialAccountHelper.CashKey(cash.Id), Date, [new("ledger", outgoing.Id)]));

        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(expected, await ReadLedgerDatesAsync(db));
        Assert.All(await db.LedgerTransactions.AsNoTracking().ToListAsync(), leg => Assert.Null(leg.TransferGroupId));
    }

    [Fact]
    public async Task Reorder_does_not_move_an_unmatched_legacy_leg_onto_an_unrelated_leg()
    {
        await using var db = CreateDb();
        var (cash, outgoing, incoming) = await SeedLegacyPairAsync(db, "Bankaya yatır: Eski hareket");
        incoming.Date = Day.AddTicks(10);
        await db.SaveChangesAsync();
        var expected = await ReadLedgerDatesAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new TransactionOrderService(db)
            .ReorderDayAsync(FinancialAccountHelper.CashKey(cash.Id), Date, [new("ledger", outgoing.Id)]));

        Assert.Equal(expected, await ReadLedgerDatesAsync(db));
        Assert.Null(await TransferPairing.FindPairAsync(db, outgoing));
    }

    [Fact]
    public async Task Reorder_does_not_attach_an_ordinary_row_sharing_a_legacy_transfers_key()
    {
        await using var db = CreateDb();
        var (cash, outgoing, incoming) = await SeedLegacyPairAsync(db, "Şube hareketi");
        outgoing.IncomeExpenseCategory!.Name = "Transfer gideri";
        incoming.IncomeExpenseCategory!.Name = "Transfer geliri";
        var ordinary = new LedgerTransaction
        {
            BankAccount = new BankAccount { Name = "Diğer banka" },
            Date = outgoing.Date,
            Amount = outgoing.Amount,
            Description = outgoing.Description,
            IncomeExpenseCategory = new() { Name = "Diğer gelirler", Type = CategoryTypeHelper.Gelir }
        };
        db.Add(ordinary);
        await db.SaveChangesAsync();
        Assert.Null(await TransferPairing.FindPairAsync(db, ordinary));

        await new TransactionOrderService(db).ReorderDayAsync(FinancialAccountHelper.CashKey(cash.Id), Date,
            [new("ledger", outgoing.Id)]);

        Assert.NotNull(outgoing.TransferGroupId);
        Assert.Equal(outgoing.TransferGroupId, incoming.TransferGroupId);
        Assert.Null(ordinary.TransferGroupId);
        Assert.Equal(Day.AddTicks(80), ordinary.Date);
    }

    [Fact]
    public async Task Reorder_rolls_back_slots_and_pairing_if_audit_write_fails()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var (cash, outgoing, _) = await SeedLegacyPairAsync(db, "Bankaya yatır: Eski hareket");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_audit_write BEFORE INSERT ON AuditLogs
            BEGIN
                SELECT RAISE(ABORT, 'Denetim kaydı yazılamadı.');
            END;
            """);

        await Assert.ThrowsAsync<DbUpdateException>(() => new TransactionOrderService(db)
            .ReorderDayAsync(FinancialAccountHelper.CashKey(cash.Id), Date, [new("ledger", outgoing.Id)]));

        db.ChangeTracker.Clear();
        Assert.All(await db.LedgerTransactions.AsNoTracking().ToListAsync(), leg =>
        {
            Assert.Equal(Day.AddTicks(80), leg.Date);
            Assert.Null(leg.TransferGroupId);
        });
    }

    private static async Task<(CashBox Cash, LedgerTransaction Outgoing, LedgerTransaction Incoming)> SeedLegacyPairAsync(
        ApplicationDbContext db, string description)
    {
        var cash = new CashBox { Name = "Kasa" };
        var outgoing = new LedgerTransaction
        {
            CashBox = cash, Date = Day.AddTicks(80), Amount = 100m, Description = description,
            IncomeExpenseCategory = new() { Name = "Diğer giderler", Type = CategoryTypeHelper.Gider }
        };
        var incoming = new LedgerTransaction
        {
            BankAccount = new BankAccount { Name = "Banka" }, Date = outgoing.Date, Amount = outgoing.Amount,
            Description = description,
            IncomeExpenseCategory = new() { Name = "Diğer gelirler", Type = CategoryTypeHelper.Gelir },
            TransferIsIncoming = true
        };
        db.AddRange(outgoing, incoming);
        await db.SaveChangesAsync();
        return (cash, outgoing, incoming);
    }

    private static Task<List<DateTime>> ReadLedgerDatesAsync(ApplicationDbContext db) =>
        db.LedgerTransactions.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => x.Date).ToListAsync();

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
