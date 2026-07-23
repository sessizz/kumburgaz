using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class UnitLedgerAndCollectionTests
{
    [Fact]
    public async Task Opening_credit_reduces_net_balance()
    {
        await using var db = CreateDb();
        var seed = await SeedUnitAsync(db, openingBalance: 10_000m, openingDate: Utc(2025, 5, 26));
        await AddInstallmentAsync(db, seed, Utc(2025, 7, 20), Utc(2026, 5, 31), 12_000m);

        var ledger = await new UnitLedgerService(db, new DuesLedgerRowService(db)).BuildAsync(seed.UnitId);

        Assert.NotNull(ledger);
        Assert.Equal(12_000m, ledger.Summary.TotalAccrual);
        Assert.Equal(10_000m, ledger.Summary.OpeningCredit);
        Assert.Equal(2_000m, ledger.Summary.NetBalance);
        Assert.Equal(2_000m, ledger.Summary.Debt);
    }

    [Fact]
    public async Task Opening_debt_increases_net_balance()
    {
        await using var db = CreateDb();
        var seed = await SeedUnitAsync(db, openingBalance: -500m, openingDate: Utc(2025, 5, 26));
        await AddInstallmentAsync(db, seed, Utc(2025, 7, 20), Utc(2026, 5, 31), 12_000m);
        await AddCollectionAsync(db, seed, Utc(2025, 9, 24), 9_500m);

        var ledger = await new UnitLedgerService(db, new DuesLedgerRowService(db)).BuildAsync(seed.UnitId);

        Assert.NotNull(ledger);
        // Genel tahsilatın 500 TL'si devir borcuna ayrıldığı için devir artık kapanmış sayılır.
        Assert.Equal(0m, ledger.Summary.OpeningDebt);
        Assert.Equal(3_000m, ledger.Summary.NetBalance);
    }

    [Fact]
    public async Task General_collection_pays_off_opening_debt_before_installments()
    {
        await using var db = CreateDb();
        var seed = await SeedUnitAsync(db, openingBalance: -750m, openingDate: Utc(2025, 7, 1));
        var installment = await AddInstallmentAsync(db, seed, Utc(2026, 7, 20), Utc(2026, 9, 30), 15_000m, period: "2026-2027");

        await AddCollectionAsync(db, seed, Utc(2025, 8, 8), 750m);

        var installmentAfter = await db.DuesInstallments.FindAsync(installment.Id);
        Assert.NotNull(installmentAfter);
        Assert.Equal(15_000m, installmentAfter.RemainingAmount);

        // Devir borcuna uygulanan 750 TL artik kalici bir CollectionAllocation satiri
        // (UnitId dolu, DuesInstallmentId bos) - tahminden ibaret degil.
        var allocation = await db.CollectionAllocations.SingleAsync();
        Assert.Equal(seed.UnitId, allocation.UnitId);
        Assert.Null(allocation.DuesInstallmentId);
        Assert.Equal(750m, allocation.AppliedAmount);

        var ledger = await new UnitLedgerService(db, new DuesLedgerRowService(db)).BuildAsync(seed.UnitId);
        Assert.NotNull(ledger);
        // Tahsis edilmiş 750 TL'lik ödeme devir borcunu tam kapattığı için OpeningDebt 0 olmalı.
        Assert.Equal(0m, ledger.Summary.OpeningDebt);
        Assert.Equal(15_000m, ledger.Summary.NetBalance);
    }

    [Fact]
    public async Task Collection_targeting_a_specific_installment_does_not_pay_opening_debt()
    {
        await using var db = CreateDb();
        // Devir borcu 750 TL; kullanici parayi devir yerine acikca 2025-2026 donemine uygular.
        var seed = await SeedUnitAsync(db, openingBalance: -750m, openingDate: Utc(2025, 7, 1));
        var installment = await AddInstallmentAsync(db, seed, Utc(2025, 7, 20), Utc(2026, 5, 31), 12_000m);

        await AddCollectionAsync(db, seed, Utc(2025, 8, 8), 750m, installment.Id);

        var allocation = await db.CollectionAllocations.SingleAsync();
        Assert.Equal(installment.Id, allocation.DuesInstallmentId);
        Assert.Null(allocation.UnitId);
        Assert.Equal(750m, allocation.AppliedAmount);

        var ledger = await new UnitLedgerService(db, new DuesLedgerRowService(db)).BuildAsync(seed.UnitId);
        Assert.NotNull(ledger);
        // Belirli bir doneme hedeflenen odeme devir borcuna sizmaz - devir hala acik kalir.
        Assert.Equal(750m, ledger.Summary.OpeningDebt);
    }

    [Fact]
    public async Task Editing_a_collection_reverses_its_opening_debt_allocation()
    {
        await using var db = CreateDb();
        var seed = await SeedUnitAsync(db, openingBalance: -750m, openingDate: Utc(2025, 7, 1));
        var collectionId = await AddCollectionAsync(db, seed, Utc(2025, 8, 8), 750m);

        var ledgerBefore = await new UnitLedgerService(db, new DuesLedgerRowService(db)).BuildAsync(seed.UnitId);
        Assert.Equal(0m, ledgerBefore!.Summary.OpeningDebt);

        // Tutari devir borcunun altina dusurecek sekilde duzenle.
        await new CollectionService(db).UpdateAsync(collectionId, new CollectionCreateViewModel
        {
            BillingGroupId = seed.BillingGroupId,
            Date = Utc(2025, 8, 8),
            Amount = 300m,
            PaymentChannel = PaymentChannel.Bank,
            ReferenceNo = "TEST-EDIT"
        });

        var allocation = await db.CollectionAllocations.SingleAsync();
        Assert.Equal(seed.UnitId, allocation.UnitId);
        Assert.Equal(300m, allocation.AppliedAmount);

        var ledgerAfter = await new UnitLedgerService(db, new DuesLedgerRowService(db)).BuildAsync(seed.UnitId);
        Assert.Equal(450m, ledgerAfter!.Summary.OpeningDebt);
    }

    [Fact]
    public async Task Dues_list_opening_balance_row_matches_ledger_after_partial_devir_payment()
    {
        await using var db = CreateDb();
        var seed = await SeedUnitAsync(db, openingBalance: -750m, openingDate: Utc(2025, 7, 1));

        // 300 TL devir borcunun bir kismini kapatir, kalan 450 TL acik kalmali.
        await AddCollectionAsync(db, seed, Utc(2025, 8, 8), 300m);

        var rows = await new DuesLedgerRowService(db).GetInstallmentRowsAsync();
        var openingRow = Assert.Single(rows, x => x.IsOpeningBalance);
        Assert.Equal(450m, openingRow.RemainingAmount);

        var ledger = await new UnitLedgerService(db, new DuesLedgerRowService(db)).BuildAsync(seed.UnitId);
        Assert.Equal(450m, ledger!.Summary.OpeningDebt);
    }

    [Fact]
    public async Task Dues_list_hides_opening_balance_row_once_devir_is_fully_paid()
    {
        await using var db = CreateDb();
        var seed = await SeedUnitAsync(db, openingBalance: -750m, openingDate: Utc(2025, 7, 1));

        await AddCollectionAsync(db, seed, Utc(2025, 8, 8), 750m);

        var rows = await new DuesLedgerRowService(db).GetInstallmentRowsAsync();
        Assert.DoesNotContain(rows, x => x.IsOpeningBalance);
    }

    [Fact]
    public async Task CollectionAdvanceAllocator_backfills_opening_debt_allocation_and_is_idempotent()
    {
        await using var db = CreateDb();
        var seed = await SeedUnitAsync(db, openingBalance: -750m, openingDate: Utc(2025, 7, 1));
        var installment = await AddInstallmentAsync(db, seed, Utc(2025, 7, 20), Utc(2026, 5, 31), 12_000m);

        // Tahsilat dogrudan eklenir (CollectionAdvanceAllocator devreye girmeden), tipki eski
        // (devir tahsisatinin hic kalici hale gelmedigi) davranisi simule eder gibi.
        db.Collections.Add(new Collection
        {
            BillingGroupId = seed.BillingGroupId,
            UnitId = seed.UnitId,
            Date = Utc(2025, 8, 8),
            Amount = 750m,
            PaymentChannel = PaymentChannel.Bank,
            ReferenceNo = "TEST-BACKFILL"
        });
        await db.SaveChangesAsync();

        await CollectionAdvanceAllocator.ApplyAsync(db);

        var allocation = await db.CollectionAllocations.SingleAsync();
        Assert.Equal(seed.UnitId, allocation.UnitId);
        Assert.Null(allocation.DuesInstallmentId);
        Assert.Equal(750m, allocation.AppliedAmount);

        var installmentAfter = await db.DuesInstallments.FindAsync(installment.Id);
        Assert.Equal(12_000m, installmentAfter!.RemainingAmount);

        // Ikinci calistirma ayni parayi tekrar devire ayirmamali (idempotent).
        await CollectionAdvanceAllocator.ApplyAsync(db);
        Assert.Equal(750m, await db.CollectionAllocations.SumAsync(x => x.AppliedAmount));
    }

    [Fact]
    public async Task Collection_is_applied_to_oldest_open_installment_first()
    {
        await using var db = CreateDb();
        var seed = await SeedUnitAsync(db);
        var older = await AddInstallmentAsync(db, seed, Utc(2025, 7, 20), Utc(2025, 8, 1), 5_000m, period: "2025-2026");
        var newer = await AddInstallmentAsync(db, seed, Utc(2025, 8, 20), Utc(2025, 9, 1), 5_000m, period: "2026-2027");

        await AddCollectionAsync(db, seed, Utc(2025, 9, 24), 7_000m);

        var olderAfter = await db.DuesInstallments.FindAsync(older.Id);
        var newerAfter = await db.DuesInstallments.FindAsync(newer.Id);
        Assert.NotNull(olderAfter);
        Assert.NotNull(newerAfter);
        Assert.Equal(0m, olderAfter.RemainingAmount);
        Assert.Equal(3_000m, newerAfter.RemainingAmount);

        var allocations = await db.CollectionAllocations
            .OrderBy(x => x.DuesInstallmentId)
            .ToListAsync();
        Assert.Equal([5_000m, 2_000m], allocations.Select(x => x.AppliedAmount).ToArray());
    }

    [Fact]
    public async Task General_collection_is_allocated_only_to_the_selected_unit_in_a_shared_billing_group()
    {
        await using var db = CreateDb();
        var seed = await SeedUnitAsync(db);
        var otherUnit = new Unit
        {
            BlockId = (await db.Units.FindAsync(seed.UnitId))!.BlockId,
            UnitNo = "C-OTHER",
            OwnerName = "Diger Malik",
            Active = true
        };
        db.Units.Add(otherUnit);
        db.BillingGroupUnits.Add(new BillingGroupUnit
        {
            BillingGroupId = seed.BillingGroupId,
            Unit = otherUnit,
            StartPeriod = "2025-2026"
        });
        await db.SaveChangesAsync();

        var otherInstallment = new DuesInstallment
        {
            UnitId = otherUnit.Id,
            BillingGroupId = seed.BillingGroupId,
            Period = "2025-2026",
            AccrualDate = Utc(2025, 7, 20),
            DueDate = Utc(2025, 9, 30),
            Amount = 12_000m,
            RemainingAmount = 12_000m,
            Status = InstallmentStatus.Open
        };
        db.DuesInstallments.Add(otherInstallment);
        await db.SaveChangesAsync();

        var oldTarget = await AddInstallmentAsync(
            db, seed, Utc(2025, 7, 20), Utc(2025, 9, 30), 12_000m, period: "2025-2026");
        oldTarget.RemainingAmount = 1_322m;
        oldTarget.Status = InstallmentStatus.PartiallyPaid;
        var newTarget = await AddInstallmentAsync(
            db, seed, Utc(2026, 7, 12), Utc(2026, 9, 30), 15_000m, period: "2026-2027");
        await db.SaveChangesAsync();

        var collectionId = await AddCollectionAsync(db, seed, Utc(2026, 7, 17), 5_025.77m);

        Assert.Equal(12_000m, (await db.DuesInstallments.FindAsync(otherInstallment.Id))!.RemainingAmount);
        Assert.Equal(0m, (await db.DuesInstallments.FindAsync(oldTarget.Id))!.RemainingAmount);
        Assert.Equal(11_296.23m, (await db.DuesInstallments.FindAsync(newTarget.Id))!.RemainingAmount);
        Assert.Equal(
            [1_322m, 3_703.77m],
            await db.CollectionAllocations
                .Where(x => x.CollectionId == collectionId)
                .OrderBy(x => x.DuesInstallment!.Period)
                .Select(x => x.AppliedAmount)
                .ToArrayAsync());
    }

    [Fact]
    public async Task Collection_targeting_a_specific_installment_does_not_apply_to_an_older_sibling()
    {
        await using var db = CreateDb();
        var seed = await SeedUnitAsync(db);
        var older = await AddInstallmentAsync(db, seed, Utc(2025, 7, 20), Utc(2025, 8, 1), 5_000m, period: "2025-2026");
        var newer = await AddInstallmentAsync(db, seed, Utc(2026, 7, 20), Utc(2026, 9, 1), 5_000m, period: "2026-2027");

        // Kullanici acikca 2026-2027 taksitini secip 3.000 TL tahsilat girer.
        await AddCollectionAsync(db, seed, Utc(2026, 8, 8), 3_000m, newer.Id);

        var olderAfter = await db.DuesInstallments.FindAsync(older.Id);
        var newerAfter = await db.DuesInstallments.FindAsync(newer.Id);
        Assert.NotNull(olderAfter);
        Assert.NotNull(newerAfter);
        Assert.Equal(5_000m, olderAfter.RemainingAmount);
        Assert.Equal(2_000m, newerAfter.RemainingAmount);

        var allocation = await db.CollectionAllocations.SingleAsync();
        Assert.Equal(newer.Id, allocation.DuesInstallmentId);
        Assert.Equal(3_000m, allocation.AppliedAmount);
    }

    [Fact]
    public async Task Overpayment_remains_as_advance_in_ledger()
    {
        await using var db = CreateDb();
        var seed = await SeedUnitAsync(db);
        var installment = await AddInstallmentAsync(db, seed, Utc(2025, 7, 20), Utc(2025, 8, 1), 8_000m);

        await AddCollectionAsync(db, seed, Utc(2025, 9, 30), 12_000m, installment.Id);

        var installmentAfter = await db.DuesInstallments.FindAsync(installment.Id);
        Assert.NotNull(installmentAfter);
        Assert.Equal(0m, installmentAfter.RemainingAmount);
        Assert.Equal(8_000m, await db.CollectionAllocations.SumAsync(x => x.AppliedAmount));

        var ledger = await new UnitLedgerService(db, new DuesLedgerRowService(db)).BuildAsync(seed.UnitId);

        Assert.NotNull(ledger);
        Assert.Equal(-4_000m, ledger.Summary.NetBalance);
        Assert.Equal(4_000m, ledger.Summary.Advance);
    }

    [Fact]
    public async Task Deleted_collection_is_soft_deleted_and_allocation_is_reversed()
    {
        await using var db = CreateDb();
        var seed = await SeedUnitAsync(db);
        var installment = await AddInstallmentAsync(db, seed, Utc(2025, 7, 20), Utc(2025, 8, 1), 8_000m);
        var collectionId = await AddCollectionAsync(db, seed, Utc(2025, 9, 30), 6_000m, installment.Id);

        await new CollectionService(db).DeleteAsync(collectionId);

        var installmentAfter = await db.DuesInstallments.FindAsync(installment.Id);
        var hiddenCollection = await db.Collections.IgnoreQueryFilters().SingleAsync(x => x.Id == collectionId);

        Assert.NotNull(installmentAfter);
        Assert.Equal(8_000m, installmentAfter.RemainingAmount);
        Assert.True(hiddenCollection.IsDeleted);
        Assert.Empty(await db.Collections.ToListAsync());
    }

    [Fact]
    public async Task Dues_debt_report_scopes_to_selected_period_but_still_includes_opening_debt()
    {
        await using var db = CreateDb();
        var seed = await SeedUnitAsync(db, openingBalance: -500m, openingDate: Utc(2025, 5, 26));
        var older = await AddInstallmentAsync(db, seed, Utc(2025, 7, 20), Utc(2025, 8, 1), 5_000m, period: "2025-2026");
        var newer = await AddInstallmentAsync(db, seed, Utc(2026, 7, 20), Utc(2026, 9, 1), 8_000m, period: "2026-2027");

        // Eski dönem tam ödenir, yeni dönemin 3.000 TL'si ödenir (5.000 TL kalır).
        await AddCollectionAsync(db, seed, Utc(2025, 8, 5), 5_000m, older.Id);
        await AddCollectionAsync(db, seed, Utc(2026, 8, 5), 3_000m, newer.Id);

        var reportingService = new ReportingService(
            db,
            new UnitLedgerService(db, new DuesLedgerRowService(db)),
            new DuesLedgerRowService(db));

        var rows = await reportingService.GetDuesDebtReportAsync(new DuesDebtReportQuery { Period = "2026-2027" });

        var row = Assert.Single(rows);
        Assert.Equal(8_000m, row.Amount);
        // 5.000 (bu dönemin kalanı) + 500 (devir borcu) = 5.500
        Assert.Equal(5_500m, row.RemainingAmount);
    }

    [Fact]
    public async Task Edited_collection_reverses_old_allocation_and_recalculates_remaining_amount()
    {
        await using var db = CreateDb();
        var seed = await SeedUnitAsync(db);
        var installment = await AddInstallmentAsync(db, seed, Utc(2025, 7, 20), Utc(2025, 8, 1), 8_000m);
        var collectionId = await AddCollectionAsync(db, seed, Utc(2025, 9, 30), 6_000m, installment.Id);

        await new CollectionService(db).UpdateAsync(collectionId, new CollectionCreateViewModel
        {
            BillingGroupId = seed.BillingGroupId,
            DuesInstallmentId = installment.Id,
            Date = Utc(2025, 9, 30),
            Amount = 3_000m,
            PaymentChannel = PaymentChannel.Bank,
            ReferenceNo = "TEST-EDIT"
        });

        var installmentAfter = await db.DuesInstallments.FindAsync(installment.Id);
        var allocation = await db.CollectionAllocations.SingleAsync(x => x.CollectionId == collectionId);

        Assert.NotNull(installmentAfter);
        Assert.Equal(5_000m, installmentAfter.RemainingAmount);
        Assert.Equal(InstallmentStatus.PartiallyPaid, installmentAfter.Status);
        Assert.Equal(3_000m, allocation.AppliedAmount);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<SeedData> SeedUnitAsync(
        ApplicationDbContext db,
        decimal openingBalance = 0m,
        DateTime? openingDate = null)
    {
        var site = new Site { Name = "Test Site" };
        var block = new Block { Site = site, Name = "A Blok" };
        var unit = new Unit
        {
            Block = block,
            UnitNo = Guid.NewGuid().ToString("N")[..8],
            OwnerName = "Test Malik",
            Active = true,
            OpeningBalance = openingBalance,
            OpeningBalanceDate = openingDate
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
        DateTime date,
        decimal amount,
        int? duesInstallmentId = null)
    {
        return await new CollectionService(db).CreateAsync(new CollectionCreateViewModel
        {
            BillingGroupId = seed.BillingGroupId,
            PreferredUnitId = seed.UnitId,
            DuesInstallmentId = duesInstallmentId,
            Date = date,
            Amount = amount,
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
