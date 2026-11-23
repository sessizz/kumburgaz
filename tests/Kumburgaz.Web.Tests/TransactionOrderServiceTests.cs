using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class TransactionOrderServiceTests
{
    private static readonly DateTime Day = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("cash:invalid")]
    [InlineData("cash:1")]
    [InlineData("bank:1")]
    public async Task Next_slot_rejects_malformed_or_missing_account_keys(string key)
    {
        await using var db = CreateDb();
        var order = new TransactionOrderService(db);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => order.NextSlotAsync(key, Day));

        Assert.Equal("Geçerli bir kasa/banka hesabı seçiniz.", error.Message);
    }

    [Theory]
    [InlineData("bank:invalid")]
    [InlineData("cash:1")]
    [InlineData("bank:1")]
    public async Task Same_day_edit_rejects_malformed_or_missing_account_keys(string key)
    {
        await using var db = CreateDb();
        var order = new TransactionOrderService(db);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            order.ResolveEditedTransactionDateAsync(key, Day, Day.AddTicks(20)));

        Assert.Equal("Geçerli bir kasa/banka hesabı seçiniz.", error.Message);
    }

    [Fact]
    public async Task Inactive_account_can_allocate_and_preserve_existing_slots()
    {
        await using var db = CreateDb();
        var bank = new BankAccount { Name = "Banka", Active = false };
        var existingDate = Day.AddTicks(20);
        db.LedgerTransactions.Add(new LedgerTransaction
        {
            BankAccount = bank,
            PaymentChannel = PaymentChannel.Bank,
            Date = existingDate
        });
        await db.SaveChangesAsync();
        var order = new TransactionOrderService(db);
        var key = FinancialAccountHelper.BankKey(bank.Id);

        Assert.Equal(Day.AddTicks(30), await order.NextSlotAsync(key, Day));
        Assert.Equal(existingDate, await order.ResolveEditedTransactionDateAsync(key, Day, existingDate));
    }

    [Fact]
    public async Task Next_slot_rejects_carry_into_the_next_day()
    {
        await using var db = CreateDb();
        var bank = new BankAccount { Name = "Banka" };
        db.LedgerTransactions.Add(new LedgerTransaction
        {
            BankAccount = bank,
            PaymentChannel = PaymentChannel.Bank,
            Date = Day.AddDays(1).AddTicks(-10)
        });
        await db.SaveChangesAsync();
        var order = new TransactionOrderService(db);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            order.NextSlotAsync(FinancialAccountHelper.BankKey(bank.Id), Day));

        Assert.Equal("Bu gün için yeni işlem sırası ayrılamadı.", error.Message);
    }

    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
