using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class CollectionAllocationPlanTests
{
    [Fact]
    public async Task Plan_matches_allocations_written_by_CreateAsync()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, openingBalance: -300m);
        var first = await AddInstallmentAsync(db, seed, "2025-2026", Utc(2025, 8, 1), 500m);
        var second = await AddInstallmentAsync(db, seed, "2026-2027", Utc(2026, 8, 1), 500m);
        var service = new CollectionService(db);
        var model = NewModel(seed, 1_000m);

        var plan = await service.PlanAllocationAsync(model);
        var collectionId = await service.CreateAsync(model);

        var written = await db.CollectionAllocations
            .Where(x => x.CollectionId == collectionId)
            .OrderBy(x => x.Id)
            .ToListAsync();

        Assert.Equal(seed.BillingGroupId, plan.BillingGroupId);
        Assert.Equal(seed.UnitId, plan.UnitId);
        Assert.Equal(0m, plan.Advance);
        Assert.Equal(3, plan.Lines.Count);
        Assert.Null(plan.Lines[0].DuesInstallmentId);
        Assert.Equal("Devir borcu", plan.Lines[0].Label);
        Assert.Equal(300m, plan.Lines[0].Applied);
        Assert.Equal(0m, plan.Lines[0].RemainingAfter);
        Assert.Equal(first.Id, plan.Lines[1].DuesInstallmentId);
        Assert.Equal("2025-2026 Cift Oda", plan.Lines[1].Label);
        Assert.Equal(500m, plan.Lines[1].Applied);
        Assert.Equal(0m, plan.Lines[1].RemainingAfter);
        Assert.Equal(second.Id, plan.Lines[2].DuesInstallmentId);
        Assert.Equal(200m, plan.Lines[2].Applied);
        Assert.Equal(300m, plan.Lines[2].RemainingAfter);

        Assert.Equal(plan.Lines.Count, written.Count);
        for (var i = 0; i < written.Count; i++)
        {
            Assert.Equal(plan.Lines[i].DuesInstallmentId, written[i].DuesInstallmentId);
            Assert.Equal(plan.Lines[i].Applied, written[i].AppliedAmount);
        }
    }

    [Fact]
    public async Task Plan_with_target_installment_keeps_overflow_as_advance()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var installment = await AddInstallmentAsync(db, seed, "2025-2026", Utc(2025, 8, 1), 500m);
        var model = NewModel(seed, 700m);
        model.DuesInstallmentId = installment.Id;

        var plan = await new CollectionService(db).PlanAllocationAsync(model);

        var line = Assert.Single(plan.Lines);
        Assert.Equal(installment.Id, line.DuesInstallmentId);
        Assert.Equal(500m, line.Applied);
        Assert.Equal(200m, plan.Advance);
    }

    [Fact]
    public async Task Plan_does_not_write()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, openingBalance: -300m);
        var installment = await AddInstallmentAsync(db, seed, "2025-2026", Utc(2025, 8, 1), 500m);
        var before = await db.CollectionAllocations.CountAsync();

        await new CollectionService(db).PlanAllocationAsync(NewModel(seed, 600m));

        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(before, await db.CollectionAllocations.CountAsync());
        Assert.Equal(500m, (await db.DuesInstallments.AsNoTracking().SingleAsync(x => x.Id == installment.Id)).RemainingAmount);
    }

    [Fact]
    public async Task BuildForUnitAsync_devir_period_targets_no_installment()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        await AddInstallmentAsync(db, seed, "2025-2026", Utc(2025, 8, 1), 500m);

        var model = await new CollectionService(db).BuildForUnitAsync(
            seed.UnitId, 100m, Utc(2025, 9, 1), null, "devir", false, null, null);

        Assert.Null(model.DuesInstallmentId);
        Assert.Equal(seed.BillingGroupId, model.BillingGroupId);
        Assert.Equal(seed.UnitId, model.PreferredUnitId);
        Assert.Equal(100m, model.Amount);
    }

    private static CollectionCreateViewModel NewModel(SeedData seed, decimal amount) => new()
    {
        BillingGroupId = seed.BillingGroupId,
        PreferredUnitId = seed.UnitId,
        Date = Utc(2025, 9, 1),
        Amount = amount,
        PaymentChannel = PaymentChannel.Bank,
        ReferenceNo = "TEST"
    };

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<SeedData> SeedAsync(ApplicationDbContext db, decimal openingBalance = 0m)
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
            OpeningBalanceDate = openingBalance == 0m ? null : Utc(2025, 5, 26)
        };
        var duesType = new DuesType { Name = "Cift Oda", Amount = 12_000m, Active = true };
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
        ApplicationDbContext db, SeedData seed, string period, DateTime dueDate, decimal amount)
    {
        var installment = new DuesInstallment
        {
            UnitId = seed.UnitId,
            BillingGroupId = seed.BillingGroupId,
            Period = period,
            AccrualDate = dueDate,
            DueDate = dueDate,
            Amount = amount,
            RemainingAmount = amount,
            Status = InstallmentStatus.Open
        };
        db.DuesInstallments.Add(installment);
        await db.SaveChangesAsync();
        return installment;
    }

    private static DateTime Utc(int year, int month, int day)
        => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private sealed record SeedData(int UnitId, int BillingGroupId);
}
