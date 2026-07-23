using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class MahsupServiceTests
{
    [Fact]
    public async Task CreateAsync_records_collection_and_ledger_on_same_cash_box_net_zero()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, installmentAmount: 5_000m);

        var mahsupId = await Service(db).CreateAsync(new MahsupService.MahsupCreateRequest(
            seed.UnitId, seed.CategoryId, 2_000m, "Su faturasi", [], "user-1", "Test Kullanici"));

        var ledger = await db.LedgerTransactions.FindAsync(mahsupId);
        Assert.NotNull(ledger);
        Assert.Equal(2_000m, ledger!.Amount);
        Assert.Equal(seed.CashBoxId, ledger.CashBoxId);
        Assert.Equal(seed.CategoryId, ledger.IncomeExpenseCategoryId);

        var mahsup = await db.MahsupIslemleri.FirstOrDefaultAsync(x => x.LedgerTransactionId == mahsupId);
        Assert.NotNull(mahsup);
        Assert.Equal(seed.UnitId, mahsup!.UnitId);

        var collection = await db.Collections.FindAsync(mahsup.CollectionId);
        Assert.NotNull(collection);
        Assert.Equal(2_000m, collection!.Amount);
        Assert.Equal(seed.CashBoxId, collection.CashBoxId);

        // Aidat borcu mahsup tutari kadar dustu.
        var installment = await db.DuesInstallments.FindAsync(seed.InstallmentId);
        Assert.Equal(3_000m, installment!.RemainingAmount);
    }

    [Fact]
    public async Task CreateAsync_throws_for_non_positive_amount()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, installmentAmount: 5_000m);

        await Assert.ThrowsAsync<MahsupService.MahsupValidationException>(() =>
            Service(db).CreateAsync(new MahsupService.MahsupCreateRequest(
                seed.UnitId, seed.CategoryId, 0m, null, [], null, null)));
    }

    [Fact]
    public async Task CreateAsync_throws_for_inactive_category()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, installmentAmount: 5_000m);
        var category = await db.IncomeExpenseCategories.FindAsync(seed.CategoryId);
        category!.Active = false;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<MahsupService.MahsupValidationException>(() =>
            Service(db).CreateAsync(new MahsupService.MahsupCreateRequest(
                seed.UnitId, seed.CategoryId, 1_000m, null, [], null, null)));
    }

    [Fact]
    public async Task CreateAsync_throws_for_income_category()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, installmentAmount: 5_000m);
        var incomeCategory = new IncomeExpenseCategory { Name = "Kira Geliri", Type = CategoryTypeHelper.Gelir, Active = true };
        db.IncomeExpenseCategories.Add(incomeCategory);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<MahsupService.MahsupValidationException>(() =>
            Service(db).CreateAsync(new MahsupService.MahsupCreateRequest(
                seed.UnitId, incomeCategory.Id, 1_000m, null, [], null, null)));
    }

    [Fact]
    public async Task DeleteAsync_removes_both_legs_and_reopens_installment()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db, installmentAmount: 5_000m);
        var mahsupId = await Service(db).CreateAsync(new MahsupService.MahsupCreateRequest(
            seed.UnitId, seed.CategoryId, 2_000m, null, [], null, null));

        var mahsup = await db.MahsupIslemleri.FirstAsync(x => x.LedgerTransactionId == mahsupId);
        var collectionId = mahsup.CollectionId;
        var deleted = await Service(db).DeleteAsync(mahsup.Id);

        // ISoftDeletable: Remove() satiri fiziksel silmez, IsDeleted=true olarak isaretler.
        // Global sorgu filtresi (!IsDeleted) FindAsync'i de etkiler; kontrol icin IgnoreQueryFilters gerekir.
        Assert.True(deleted);
        Assert.True((await db.LedgerTransactions.IgnoreQueryFilters().FirstAsync(x => x.Id == mahsupId)).IsDeleted);
        Assert.True((await db.Collections.IgnoreQueryFilters().FirstAsync(x => x.Id == collectionId)).IsDeleted);
        Assert.True((await db.MahsupIslemleri.IgnoreQueryFilters().FirstAsync(x => x.Id == mahsup.Id)).IsDeleted);

        var installment = await db.DuesInstallments.FindAsync(seed.InstallmentId);
        Assert.Equal(5_000m, installment!.RemainingAmount);
    }

    [Fact]
    public async Task DeleteAsync_returns_false_for_unknown_id()
    {
        await using var db = CreateDb();
        Assert.False(await Service(db).DeleteAsync(999));
    }

    private static MahsupService Service(ApplicationDbContext db)
        => new(db, new CollectionService(db), new ImageAttachmentService());

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed record SeedResult(int UnitId, int CategoryId, int CashBoxId, int InstallmentId);

    private static async Task<SeedResult> SeedAsync(ApplicationDbContext db, decimal installmentAmount)
    {
        var site = new Site { Name = "Test Site" };
        var block = new Block { Site = site, Name = "A Blok" };
        var unit = new Unit { Block = block, UnitNo = "1", Active = true };
        var duesType = new DuesType { Name = "Standart", Amount = installmentAmount, Active = true };
        var billingGroup = new BillingGroup
        {
            Name = "Standart Daireler",
            DuesType = duesType,
            EffectiveStartPeriod = "2025-2026",
            Active = true
        };
        var billingGroupUnit = new BillingGroupUnit { BillingGroup = billingGroup, Unit = unit, StartPeriod = "2025-2026" };
        var category = new IncomeExpenseCategory { Name = "Su", Type = CategoryTypeHelper.Gider, Active = true };
        var cashBox = new CashBox { Name = "Ana Kasa", Active = true, OpeningBalance = 0m };

        db.AddRange(site, block, unit, duesType, billingGroup, billingGroupUnit, category, cashBox);
        await db.SaveChangesAsync();

        var installment = new DuesInstallment
        {
            UnitId = unit.Id,
            BillingGroupId = billingGroup.Id,
            Period = "2025-2026",
            AccrualDate = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            DueDate = new DateTime(2025, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            Amount = installmentAmount,
            RemainingAmount = installmentAmount,
            Status = InstallmentStatus.Open
        };
        db.DuesInstallments.Add(installment);
        await db.SaveChangesAsync();

        return new SeedResult(unit.Id, category.Id, cashBox.Id, installment.Id);
    }
}
