using Kumburgaz.Web.Controllers;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class AccountsControllerTests
{
    [Fact]
    public async Task Detail_lists_a_split_collection_once_with_its_full_amount()
    {
        await using var db = CreateDb();
        var account = new Account { Name = "Erdogan Demirhan", AccountType = AccountType.Owner };
        var site = new Site { Name = "Test Site" };
        var block = new Block { Site = site, Name = "C Blok" };
        var unit = new Unit { Block = block, UnitNo = "27", OwnerName = account.Name, Active = true };
        var unitAccount = new UnitAccount
        {
            Account = account,
            Unit = unit,
            Role = UnitAccountRole.Owner,
            Active = true
        };
        var duesType = new DuesType { Name = "Cift Oda", Amount = 15_000m, Active = true };
        var billingGroup = new BillingGroup
        {
            Name = "Cift Oda",
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
        db.AddRange(account, site, block, unit, unitAccount, duesType, billingGroup, billingGroupUnit);
        await db.SaveChangesAsync();

        var oldInstallment = new DuesInstallment
        {
            BillingGroupId = billingGroup.Id,
            UnitId = unit.Id,
            ResponsibleAccountId = account.Id,
            Period = "2025-2026",
            AccrualDate = Utc(2025, 7, 20),
            DueDate = Utc(2025, 9, 30),
            Amount = 12_000m,
            RemainingAmount = 0m,
            Status = InstallmentStatus.Paid
        };
        var newInstallment = new DuesInstallment
        {
            BillingGroupId = billingGroup.Id,
            UnitId = unit.Id,
            ResponsibleAccountId = account.Id,
            Period = "2026-2027",
            AccrualDate = Utc(2026, 7, 12),
            DueDate = Utc(2026, 9, 30),
            Amount = 15_000m,
            RemainingAmount = 11_296.23m,
            Status = InstallmentStatus.PartiallyPaid
        };
        var collection = new Collection
        {
            BillingGroupId = billingGroup.Id,
            UnitId = unit.Id,
            Date = Utc(2026, 7, 17),
            Amount = 5_025.77m,
            PaymentChannel = PaymentChannel.Cash
        };
        db.AddRange(oldInstallment, newInstallment, collection);
        await db.SaveChangesAsync();
        db.CollectionAllocations.AddRange(
            new CollectionAllocation
            {
                CollectionId = collection.Id,
                DuesInstallmentId = oldInstallment.Id,
                AppliedAmount = 1_322m
            },
            new CollectionAllocation
            {
                CollectionId = collection.Id,
                DuesInstallmentId = newInstallment.Id,
                AppliedAmount = 3_703.77m
            });
        await db.SaveChangesAsync();

        var duesLedger = new DuesLedgerRowService(db);
        var controller = new AccountsController(
            db,
            new UnitLedgerService(db, duesLedger),
            null!,
            duesLedger);

        var result = Assert.IsType<ViewResult>(await controller.Detail(account.Id));
        var model = Assert.IsType<AccountDetailViewModel>(result.Model);
        var row = Assert.Single(model.RecentCollections);
        Assert.Equal(5_025.77m, row.Amount);
        Assert.Contains("C Blok-27", row.Description);
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
