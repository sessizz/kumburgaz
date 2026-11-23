using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class CashBankTransferServiceTests
{
    private static readonly DateTime Day = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Create_writes_two_legs_with_shared_group_id_and_prefix()
    {
        await using var db = CreateDb();
        var request = await SeedAccountsAsync(db);

        var result = await CreateService(db).CreateAsync(request);

        var legs = await db.LedgerTransactions.AsNoTracking().ToListAsync();
        Assert.Equal(2, legs.Count);
        Assert.NotEqual(Guid.Empty, result.GroupId);
        Assert.All(legs, leg =>
        {
            Assert.True(leg.IsTransfer);
            Assert.Equal(result.GroupId, leg.TransferGroupId);
            Assert.Equal(51_600m, leg.Amount);
            Assert.Equal("Bankaya yatır: ATM", leg.Description);
            Assert.Null(leg.IncomeExpenseCategoryId);
        });

        var outgoing = Assert.Single(legs, leg => leg.Id == result.OutgoingId);
        Assert.False(outgoing.TransferIsIncoming);
        Assert.Equal(PaymentChannel.Cash, outgoing.PaymentChannel);
        Assert.Equal(request.FromAccountKey, FinancialAccountHelper.BuildKey(outgoing.CashBoxId, outgoing.BankAccountId));
        Assert.Null(outgoing.BankAccountId);

        var incoming = Assert.Single(legs, leg => leg.Id == result.IncomingId);
        Assert.True(incoming.TransferIsIncoming);
        Assert.Equal(PaymentChannel.Bank, incoming.PaymentChannel);
        Assert.Equal(request.ToAccountKey, FinancialAccountHelper.BuildKey(incoming.CashBoxId, incoming.BankAccountId));
        Assert.Null(incoming.CashBoxId);
        Assert.Equal(outgoing.Date, incoming.Date);
    }

    [Fact]
    public async Task Create_uses_para_transferi_prefix_for_bank_to_cash()
    {
        await using var db = CreateDb();
        var request = await SeedAccountsAsync(db);

        await CreateService(db).CreateAsync(request with
        {
            FromAccountKey = request.ToAccountKey,
            ToAccountKey = request.FromAccountKey,
            Description = " "
        });

        Assert.All(await db.LedgerTransactions.AsNoTracking().ToListAsync(),
            leg => Assert.Equal("Para transferi", leg.Description));
    }

    [Fact]
    public async Task Create_takes_next_slot_of_source_account_for_the_day()
    {
        await using var db = CreateDb();
        var request = await SeedAccountsAsync(db);
        var cash = await db.CashBoxes.SingleAsync();
        var bank = await db.BankAccounts.SingleAsync();
        db.Collections.Add(new Collection { CashBoxId = cash.Id, Date = Day.AddTicks(10) });
        db.LedgerTransactions.AddRange(
            new LedgerTransaction { CashBoxId = cash.Id, Date = Day.AddTicks(20) },
            new LedgerTransaction { CashBoxId = cash.Id, Date = Day.AddDays(1).AddTicks(50) },
            new LedgerTransaction { BankAccountId = bank.Id, Date = Day.AddTicks(80) });
        await db.SaveChangesAsync();

        var result = await CreateService(db).CreateAsync(request);

        var legs = await db.LedgerTransactions.AsNoTracking()
            .Where(leg => leg.TransferGroupId == result.GroupId).ToListAsync();
        Assert.Equal(2, legs.Count);
        Assert.All(legs, leg =>
        {
            Assert.Equal(Day.AddTicks(30), leg.Date);
            Assert.Equal(DateTimeKind.Utc, leg.Date.Kind);
        });
    }

    [Fact]
    public async Task Preview_writes_nothing()
    {
        await using var db = CreateDb();
        var request = await SeedAccountsAsync(db);

        var preview = await CreateService(db).PreviewAsync(request);

        Assert.Equal(new TransferPreview(request.FromAccountKey, "Kasa", request.ToAccountKey,
            "Banka - Merkez", 51_600m, Day, "Bankaya yatır: ATM"), preview);
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Empty(await db.LedgerTransactions.ToListAsync());
    }

    [Fact]
    public async Task Create_rejects_same_account()
    {
        await using var db = CreateDb();
        var request = await SeedAccountsAsync(db);
        var cash = await db.CashBoxes.SingleAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(db).CreateAsync(request with { ToAccountKey = $"CASH:0{cash.Id}" }));

        Assert.Equal("Kaynak ve hedef hesap aynı olamaz.", error.Message);
        Assert.Empty(await db.LedgerTransactions.ToListAsync());
    }

    [Fact]
    public async Task Create_rejects_inactive_account()
    {
        await using var db = CreateDb();
        var request = await SeedAccountsAsync(db);
        var bank = await db.BankAccounts.SingleAsync();
        bank.Active = false;
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(db).CreateAsync(request));

        Assert.Equal("Geçerli bir kasa/banka hesabı seçiniz.", error.Message);
        Assert.Empty(await db.LedgerTransactions.ToListAsync());
    }

    [Fact]
    public async Task Update_changes_both_legs()
    {
        await using var db = CreateDb();
        var request = await SeedAccountsAsync(db);
        var service = CreateService(db);
        var result = await service.CreateAsync(request);
        var oldIncoming = await db.LedgerTransactions.SingleAsync(leg => leg.Id == result.IncomingId);
        var editedDate = oldIncoming.Date.AddTicks(40);
        oldIncoming.Date = editedDate;
        var newBank = new BankAccount { Name = "Diğer Banka", OpeningBalanceDate = Day };
        db.BankAccounts.Add(newBank);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await service.UpdateAsync(result.IncomingId, request with
        {
            ToAccountKey = FinancialAccountHelper.BankKey(newBank.Id),
            Amount = 300m,
            Description = "Güncellendi"
        });

        var legs = await db.LedgerTransactions.AsNoTracking().ToListAsync();
        Assert.Equal(2, legs.Count);
        Assert.All(legs, leg =>
        {
            Assert.Equal(300m, leg.Amount);
            Assert.Equal(editedDate, leg.Date);
            Assert.Equal("Bankaya yatır: Güncellendi", leg.Description);
            Assert.Equal(result.GroupId, leg.TransferGroupId);
        });
        var incoming = Assert.Single(legs, leg => leg.Id == result.IncomingId);
        Assert.True(incoming.TransferIsIncoming);
        Assert.Equal(newBank.Id, incoming.BankAccountId);
        var outgoing = Assert.Single(legs, leg => leg.Id == result.OutgoingId);
        Assert.False(outgoing.TransferIsIncoming);
        Assert.Equal(request.FromAccountKey, FinancialAccountHelper.BuildKey(outgoing.CashBoxId, outgoing.BankAccountId));
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<TransferRequest> SeedAccountsAsync(ApplicationDbContext db)
    {
        var cash = new CashBox { Name = "Kasa", OpeningBalanceDate = Day };
        var bank = new BankAccount { Name = "Banka", Branch = "Merkez", OpeningBalanceDate = Day };
        db.AddRange(cash, bank);
        await db.SaveChangesAsync();

        return new TransferRequest(FinancialAccountHelper.CashKey(cash.Id),
            FinancialAccountHelper.BankKey(bank.Id), 51_600m, Day, "ATM");
    }

    private static CashBankTransferService CreateService(ApplicationDbContext db) =>
        new(db, new TransactionOrderService(db));
}
