using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class CashBankDetailServiceTests
{
    private static readonly DateTime Date = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Para transferi", TxKind.Girdi, 100)]
    [InlineData("Bankaya yatır", TxKind.Girdi, 100)]
    [InlineData("Para transferi: Açıklama", TxKind.Transfer, -100)]
    [InlineData("Bankaya yatır: Açıklama", TxKind.Transfer, -100)]
    public async Task Display_preserves_legacy_description_classification(string description, TxKind kind, int amount)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var bank = new BankAccount { Name = "Banka" };
        db.Add(new LedgerTransaction
        {
            Date = Date,
            Amount = 100m,
            BankAccount = bank,
            PaymentChannel = PaymentChannel.Bank,
            Description = description,
            IncomeExpenseCategory = new() { Name = "Diğer gelirler", Type = CategoryTypeHelper.Gelir }
        });
        await db.SaveChangesAsync();

        var detail = await new CashBankDetailService(db, new DuesLedgerRowService(db))
            .BuildAsync("bank", bank.Id, new CashBankDetailQuery { Range = "all" });

        var row = Assert.Single(detail!.Groups.SelectMany(g => g.Items));
        Assert.Equal(kind, row.Kind);
        Assert.Equal((decimal)amount, row.Amount);
    }

    [Fact]
    public async Task Display_fetches_grouped_and_legacy_counterparts_in_one_query()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commands = new List<string>();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .LogTo(commands.Add, [RelationalEventId.CommandExecuted])
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var bank = new BankAccount { Name = "Banka" };
        var groupedCash = new CashBox { Name = "Kasa 1" };
        var legacyCash = new CashBox { Name = "Kasa 2" };
        var income = new IncomeExpenseCategory { Name = "Transfer geliri", Type = CategoryTypeHelper.Gelir };
        var expense = new IncomeExpenseCategory { Name = "Transfer gideri", Type = CategoryTypeHelper.Gider };
        var groupId = Guid.NewGuid();
        var grouped = new LedgerTransaction
        {
            Date = Date.AddTicks(10), Amount = 100m, BankAccount = bank, PaymentChannel = PaymentChannel.Bank,
            IsTransfer = true, TransferIsIncoming = true, TransferGroupId = groupId
        };
        var legacy = new LedgerTransaction
        {
            Date = Date, Amount = 200m, BankAccount = bank, PaymentChannel = PaymentChannel.Bank,
            IncomeExpenseCategory = income
        };
        db.AddRange(grouped, legacy,
            new LedgerTransaction
            {
                Date = Date, Amount = 100m, CashBox = groupedCash, PaymentChannel = PaymentChannel.Cash,
                IsTransfer = true, TransferGroupId = groupId
            },
            new LedgerTransaction
            {
                Date = Date, Amount = 200m, CashBox = legacyCash, PaymentChannel = PaymentChannel.Cash,
                IncomeExpenseCategory = expense, TransferIsIncoming = true
            });
        await db.SaveChangesAsync();
        commands.Clear();

        var detail = await new CashBankDetailService(db, new DuesLedgerRowService(db))
            .BuildAsync("bank", bank.Id, new CashBankDetailQuery { Range = "all" });

        Assert.Equal(2, commands.Count(command => command.Contains("FROM \"LedgerTransactions\"", StringComparison.Ordinal)));
        var rows = detail!.Groups.SelectMany(g => g.Items).ToDictionary(x => x.Id);
        Assert.Equal($"cash:{groupedCash.Id}", rows[grouped.Id].ToAccountKey);
        Assert.Equal($"cash:{legacyCash.Id}", rows[legacy.Id].ToAccountKey);
        Assert.Equal(TxKind.Transfer, rows[legacy.Id].Kind);
        Assert.Equal(200m, rows[legacy.Id].Amount);
        Assert.Equal(300m, detail.Balance);
    }
}
