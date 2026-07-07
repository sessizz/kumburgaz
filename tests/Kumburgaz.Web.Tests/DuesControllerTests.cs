using Kumburgaz.Web.Controllers;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class DuesControllerTests
{
    [Fact]
    public async Task Index_applies_unallocated_collection_credit_to_open_dues()
    {
        await using var db = CreateDb();
        var site = new Site { Name = "Test Site" };
        var block = new Block { Site = site, Name = "B Blok" };
        var unit = new Unit
        {
            Block = block,
            UnitNo = "22",
            OwnerName = "NURI HURI",
            Active = true
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
        var bank = new BankAccount
        {
            Name = "Akbank",
            OpeningBalanceDate = Utc(2025, 1, 1),
            Active = true
        };
        db.AddRange(site, block, unit, duesType, billingGroup, billingGroupUnit, bank);
        await db.SaveChangesAsync();

        db.DuesInstallments.Add(new DuesInstallment
        {
            BillingGroupId = billingGroup.Id,
            UnitId = unit.Id,
            Period = "2025-2026",
            AccrualDate = Utc(2025, 7, 20),
            DueDate = Utc(2026, 5, 31),
            Amount = 12_000m,
            RemainingAmount = 12_000m,
            Status = InstallmentStatus.Open
        });
        db.Collections.Add(new Collection
        {
            BillingGroupId = billingGroup.Id,
            UnitId = unit.Id,
            Date = Utc(2025, 9, 30),
            Amount = 12_000m,
            PaymentChannel = PaymentChannel.Bank,
            BankAccountId = bank.Id,
            ReferenceNo = "MAKBUZ-1"
        });
        await db.SaveChangesAsync();

        var controller = new DuesController(
            db,
            new CollectionService(db),
            new AccountAssignmentService(db));

        var result = await controller.Index(q: "NURI HURI") as ViewResult;

        Assert.NotNull(result);
        var model = Assert.IsType<DuesIndexViewModel>(result.Model);
        var row = Assert.Single(model.DuesItems);
        Assert.True(row.IsPaid);
        Assert.Equal(0m, row.RemainingAmount);
        Assert.Equal(Utc(2025, 9, 30), row.PaymentOrDueDate);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static DateTime Utc(int year, int month, int day)
    {
        return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    }
}
