using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class ConsistencyCheckServiceTests
{
    [Fact]
    public async Task Clean_unit_ledger_has_no_consistency_issue()
    {
        await using var db = CreateDb();
        var seed = await SeedUnitAsync(db);
        var bank = await AddBankAsync(db);
        var installment = await AddInstallmentAsync(db, seed, Utc(2025, 7, 20), Utc(2025, 8, 1), 8_000m);

        await AddCollectionAsync(db, seed, bank.Id, Utc(2025, 9, 30), 8_000m, installment.Id);

        var count = await CreateService(db).RunAsync();

        Assert.Equal(0, count);
        Assert.Empty(await db.ConsistencyCheckResults.Where(x => !x.Resolved).ToListAsync());
    }

    [Fact]
    public async Task Unapplied_advance_with_open_debt_is_reported()
    {
        await using var db = CreateDb();
        var seed = await SeedUnitAsync(db);
        var bank = await AddBankAsync(db);
        var first = await AddInstallmentAsync(db, seed, Utc(2025, 7, 20), Utc(2025, 8, 1), 8_000m);

        await AddCollectionAsync(db, seed, bank.Id, Utc(2025, 9, 30), 10_000m, first.Id);
        await AddInstallmentAsync(db, seed, Utc(2026, 7, 20), Utc(2026, 8, 1), 1_000m, period: "2026-2027");

        await CreateService(db).RunAsync();

        var issue = await db.ConsistencyCheckResults.SingleAsync(x => x.CheckName == "Avans açık borca uygulanmamış");
        Assert.Equal("Warning", issue.Severity);
        Assert.Equal(2_000m, issue.Difference);
    }

    [Fact]
    public async Task Transfer_without_counterpart_is_reported()
    {
        await using var db = CreateDb();
        var bank = await AddBankAsync(db);
        db.LedgerTransactions.Add(new LedgerTransaction
        {
            Date = Utc(2026, 7, 1),
            Amount = 5_000m,
            PaymentChannel = PaymentChannel.Bank,
            BankAccountId = bank.Id,
            Description = "Para transferi",
            IsTransfer = true,
            TransferIsIncoming = true
        });
        await db.SaveChangesAsync();

        await CreateService(db).RunAsync();

        var issue = await db.ConsistencyCheckResults.SingleAsync(x => x.CheckName == "Transfer karşı hareketi");
        Assert.Equal("Error", issue.Severity);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ConsistencyCheckService CreateService(ApplicationDbContext db)
    {
        return new ConsistencyCheckService(db, new UnitLedgerService(db));
    }

    private static async Task<SeedData> SeedUnitAsync(ApplicationDbContext db)
    {
        var site = new Site { Name = "Test Site" };
        var block = new Block { Site = site, Name = "A Blok" };
        var unit = new Unit
        {
            Block = block,
            UnitNo = Guid.NewGuid().ToString("N")[..8],
            OwnerName = "Test Malik",
            Active = true
        };
        var duesType = new DuesType
        {
            Name = "Cift Oda",
            Amount = 12_000m,
            Active = true
        };
        var billingGroup = new BillingGroup
        {
            Name = "Buyuk Daireler",
            DuesType = duesType,
            EffectiveStartPeriod = "2025-2026",
            Active = true
        };
        var billingGroupUnit = new BillingGroupUnit
        {
            BillingGroup = billingGroup,
            Unit = unit,
            StartPeriod = "2025-2026"
        };

        db.AddRange(site, block, unit, duesType, billingGroup, billingGroupUnit);
        await db.SaveChangesAsync();

        return new SeedData(unit.Id, billingGroup.Id);
    }

    private static async Task<BankAccount> AddBankAsync(ApplicationDbContext db)
    {
        var bank = new BankAccount
        {
            Name = "Akbank",
            Branch = "Test",
            OpeningBalanceDate = Utc(2025, 1, 1),
            Active = true
        };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        return bank;
    }

    private static async Task<DuesInstallment> AddInstallmentAsync(
        ApplicationDbContext db,
        SeedData seed,
        DateTime accrualDate,
        DateTime dueDate,
        decimal amount,
        string period = "2025-2026")
    {
        var installment = new DuesInstallment
        {
            UnitId = seed.UnitId,
            BillingGroupId = seed.BillingGroupId,
            Period = period,
            AccrualDate = accrualDate,
            DueDate = dueDate,
            Amount = amount,
            RemainingAmount = amount,
            Status = InstallmentStatus.Open
        };

        db.DuesInstallments.Add(installment);
        await db.SaveChangesAsync();
        return installment;
    }

    private static async Task<int> AddCollectionAsync(
        ApplicationDbContext db,
        SeedData seed,
        int bankAccountId,
        DateTime date,
        decimal amount,
        int? duesInstallmentId = null)
    {
        return await new CollectionService(db).CreateAsync(new CollectionCreateViewModel
        {
            BillingGroupId = seed.BillingGroupId,
            DuesInstallmentId = duesInstallmentId,
            Date = date,
            Amount = amount,
            AccountKey = FinancialAccountHelper.BankKey(bankAccountId),
            PaymentChannel = PaymentChannel.Bank,
            ReferenceNo = "TEST"
        });
    }

    private static DateTime Utc(int year, int month, int day)
    {
        return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    }

    private sealed record SeedData(int UnitId, int BillingGroupId);
}
