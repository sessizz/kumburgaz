using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Tests;

public class BillingRulesTests
{
    [Fact]
    public async Task MergedUnits_ShouldGenerateSingleDoubleRoomInstallment()
    {
        await using var db = CreateDb();
        SeedCoreData(db);

        var service = new DuesGenerationService(db);
        await service.GenerateForPeriodAsync("2025-2026", new DateTime(2025, 7, 1), new DateTime(2025, 7, 31));

        var mergedGroupInstallment = await db.DuesInstallments
            .SingleAsync(x => x.BillingGroupId == 1 && x.Period == "2025-2026");
        Assert.Equal(12000m, mergedGroupInstallment.Amount);
    }

    [Fact]
    public async Task CombinedUnit_ShouldGenerateSingleInstallmentForCombinedUnit()
    {
        await using var db = CreateDb();
        SeedCoreData(db);

        db.Units.Add(new Unit
        {
            Id = 3,
            BlockId = 1,
            UnitNo = "1+2",
            OwnerName = "Ornek Malik",
            Active = true,
            IsCombined = true
        });
        db.CombinedUnitMembers.AddRange(
            new CombinedUnitMember { Id = 1, CombinedUnitId = 3, ComponentUnitId = 1 },
            new CombinedUnitMember { Id = 2, CombinedUnitId = 3, ComponentUnitId = 2 }
        );
        db.BillingGroups.Add(new BillingGroup
        {
            Id = 2,
            Name = "A1-A2 Cift Oda",
            DuesTypeId = 2,
            EffectiveStartPeriod = "2025-2026",
            Active = true,
            IsMerged = false
        });
        db.BillingGroupUnits.Add(new BillingGroupUnit
        {
            Id = 3,
            BillingGroupId = 2,
            UnitId = 3,
            StartPeriod = "2025-2026"
        });
        await db.SaveChangesAsync();

        var service = new DuesGenerationService(db);
        await service.GenerateForPeriodAsync("2025-2026", new DateTime(2025, 7, 1), new DateTime(2025, 7, 31));

        var combinedInstallment = await db.DuesInstallments
            .SingleAsync(x => x.BillingGroupId == 2 && x.Period == "2025-2026");

        Assert.Equal(3, combinedInstallment.UnitId);
        Assert.Equal(12000m, combinedInstallment.Amount);
    }

    [Fact]
    public async Task InactiveUnits_ShouldNotGenerateInstallments()
    {
        await using var db = CreateDb();
        SeedCoreData(db);

        db.BillingGroups.Add(new BillingGroup
        {
            Id = 2,
            Name = "Pasif Daire Grubu",
            DuesTypeId = 1,
            EffectiveStartPeriod = "2025-2026",
            Active = true,
            IsMerged = false
        });
        db.Units.Add(new Unit { Id = 3, BlockId = 1, UnitNo = "3", Active = false });
        db.BillingGroupUnits.Add(new BillingGroupUnit
        {
            Id = 3,
            BillingGroupId = 2,
            UnitId = 3,
            StartPeriod = "2025-2026"
        });
        await db.SaveChangesAsync();

        var service = new DuesGenerationService(db);
        await service.GenerateForPeriodAsync("2025-2026", new DateTime(2025, 7, 1), new DateTime(2025, 7, 31));

        var inactiveInstallmentExists = await db.DuesInstallments
            .AnyAsync(x => x.BillingGroupId == 2 && x.UnitId == 3);

        Assert.False(inactiveInstallmentExists);
    }

    [Fact]
    public async Task BillingGroup_ShouldRejectInactiveUnits()
    {
        await using var db = CreateDb();
        SeedCoreData(db);
        db.Units.Add(new Unit { Id = 3, BlockId = 1, UnitNo = "3", Active = false });
        await db.SaveChangesAsync();

        var service = new BillingGroupService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateOrUpdateAsync(new BillingGroupFormViewModel
            {
                Name = "Pasif Daire Grubu",
                DuesTypeId = 1,
                EffectiveStartPeriod = "2025-2026",
                Active = true,
                SelectedUnitIds = [3]
            }));

        Assert.Contains("aktif daireler", ex.Message);
    }

    [Fact]
    public async Task Collection_ShouldApplyToOldestInstallmentFirst()
    {
        await using var db = CreateDb();
        SeedCoreData(db);

        db.DuesInstallments.AddRange(
            new DuesInstallment
            {
                Id = 1, BillingGroupId = 1, Period = "2024-2025", AccrualDate = new DateTime(2024, 7, 1), DueDate = new DateTime(2024, 7, 31),
                Amount = 12000m, RemainingAmount = 12000m
            },
            new DuesInstallment
            {
                Id = 2, BillingGroupId = 1, Period = "2025-2026", AccrualDate = new DateTime(2025, 7, 1), DueDate = new DateTime(2025, 7, 31),
                Amount = 12000m, RemainingAmount = 12000m
            }
        );
        await db.SaveChangesAsync();

        var service = new CollectionService(db);
        await service.CreateAsync(new CollectionCreateViewModel
        {
            BillingGroupId = 1,
            Date = new DateTime(2026, 2, 15),
            Amount = 15000m,
            PaymentChannel = PaymentChannel.Bank
        });

        var jan = await db.DuesInstallments.FindAsync(1);
        var feb = await db.DuesInstallments.FindAsync(2);

        Assert.NotNull(jan);
        Assert.NotNull(feb);
        Assert.Equal(0m, jan!.RemainingAmount);
        Assert.Equal(9000m, feb!.RemainingAmount);
        Assert.Equal(InstallmentStatus.Paid, jan.Status);
        Assert.Equal(InstallmentStatus.PartiallyPaid, feb.Status);
    }

    [Fact]
    public async Task Collection_ShouldApplySelectedUnitInstallmentFirst()
    {
        await using var db = CreateDb();
        SeedCoreData(db);

        db.DuesInstallments.AddRange(
            new DuesInstallment
            {
                Id = 1, BillingGroupId = 1, UnitId = 1, Period = "2025-2026", AccrualDate = new DateTime(2025, 7, 1), DueDate = new DateTime(2025, 7, 31),
                Amount = 12000m, RemainingAmount = 12000m
            },
            new DuesInstallment
            {
                Id = 2, BillingGroupId = 1, UnitId = 2, Period = "2025-2026", AccrualDate = new DateTime(2025, 7, 1), DueDate = new DateTime(2025, 7, 31),
                Amount = 12000m, RemainingAmount = 12000m
            }
        );
        await db.SaveChangesAsync();

        var service = new CollectionService(db);
        await service.CreateAsync(new CollectionCreateViewModel
        {
            BillingGroupId = 1,
            DuesInstallmentId = 2,
            Date = new DateTime(2026, 2, 15),
            Amount = 12000m,
            PaymentChannel = PaymentChannel.Bank
        });

        var firstUnitInstallment = await db.DuesInstallments.FindAsync(1);
        var secondUnitInstallment = await db.DuesInstallments.FindAsync(2);
        var collection = await db.Collections.SingleAsync();

        Assert.NotNull(firstUnitInstallment);
        Assert.NotNull(secondUnitInstallment);
        Assert.Equal(12000m, firstUnitInstallment!.RemainingAmount);
        Assert.Equal(0m, secondUnitInstallment!.RemainingAmount);
        Assert.Equal(InstallmentStatus.Open, firstUnitInstallment.Status);
        Assert.Equal(InstallmentStatus.Paid, secondUnitInstallment.Status);
        Assert.Equal(2, collection.UnitId);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static void SeedCoreData(ApplicationDbContext db)
    {
        db.Sites.Add(new Site { Id = 1, Name = "Site" });
        db.Blocks.Add(new Block { Id = 1, SiteId = 1, Name = "A Blok" });
        db.Units.AddRange(
            new Unit { Id = 1, BlockId = 1, UnitNo = "1", Active = true },
            new Unit { Id = 2, BlockId = 1, UnitNo = "2", Active = true }
        );
        db.DuesTypes.AddRange(
            new DuesType { Id = 1, Name = "Tek Oda", Amount = 9000m, Active = true },
            new DuesType { Id = 2, Name = "Cift Oda", Amount = 12000m, Active = true }
        );
        db.BillingGroups.Add(new BillingGroup
        {
            Id = 1,
            Name = "A1-A2 Birlesik",
            DuesTypeId = 2,
            EffectiveStartPeriod = "2025-2026",
            Active = true,
            IsMerged = true
        });
        db.BillingGroupUnits.AddRange(
            new BillingGroupUnit { Id = 1, BillingGroupId = 1, UnitId = 1, StartPeriod = "2025-2026" },
            new BillingGroupUnit { Id = 2, BillingGroupId = 1, UnitId = 2, StartPeriod = "2025-2026" }
        );
        db.SaveChanges();
    }
}
