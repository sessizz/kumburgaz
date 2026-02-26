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
        await service.GenerateForPeriodAsync("2025-2026", new DateTime(2025, 7, 31));

        var mergedGroupInstallment = await db.DuesInstallments
            .SingleAsync(x => x.BillingGroupId == 1 && x.Period == "2025-2026");
        Assert.Equal(12000m, mergedGroupInstallment.Amount);
    }

    [Fact]
    public async Task Collection_ShouldApplyToOldestInstallmentFirst()
    {
        await using var db = CreateDb();
        SeedCoreData(db);

        db.DuesInstallments.AddRange(
            new DuesInstallment
            {
                Id = 1, BillingGroupId = 1, Period = "2024-2025", DueDate = new DateTime(2024, 7, 31),
                Amount = 12000m, RemainingAmount = 12000m
            },
            new DuesInstallment
            {
                Id = 2, BillingGroupId = 1, Period = "2025-2026", DueDate = new DateTime(2025, 7, 31),
                Amount = 12000m, RemainingAmount = 12000m
            }
        );
        await db.SaveChangesAsync();

        var service = new CollectionService(db);
        await service.CreateAsync(new CollectionCreateViewModel
        {
            BillingGroupId = 1,
            UnitId = 1,
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
            Active = true
        });
        db.BillingGroupUnits.AddRange(
            new BillingGroupUnit { Id = 1, BillingGroupId = 1, UnitId = 1, StartPeriod = "2025-2026" },
            new BillingGroupUnit { Id = 2, BillingGroupId = 1, UnitId = 2, StartPeriod = "2025-2026" }
        );
        db.SaveChanges();
    }
}
