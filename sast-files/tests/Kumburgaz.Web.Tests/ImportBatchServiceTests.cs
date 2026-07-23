using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class ImportBatchServiceTests
{
    [Fact]
    public async Task Committed_duplicate_key_is_detected()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var key = ImportBatchService.BuildNormalizedKey("ledger", "gider", 1, "bank:1", "2026-07-01", "15.96", "EFT");

        var batch = await service.CreateAsync("gider", null, null, "giderler.csv");
        await service.AddRowAsync(batch, 2, new { Description = "EFT" }, key, ImportRowStatus.Committed);
        await service.CommitAsync(batch);

        Assert.True(await service.HasCommittedDuplicateAsync(key));
        Assert.False(await service.HasCommittedDuplicateAsync(ImportBatchService.BuildNormalizedKey("other")));
    }

    [Fact]
    public async Task Rollback_soft_deletes_created_ledger_transaction()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var category = new IncomeExpenseCategory
        {
            Name = "Banka Masrafi",
            Type = CategoryTypeHelper.Gider,
            Active = true
        };
        var transaction = new LedgerTransaction
        {
            Date = Utc(2026, 7, 1),
            IncomeExpenseCategory = category,
            Amount = 15.96m,
            PaymentChannel = PaymentChannel.Bank,
            Description = "EFT MASRAFI"
        };

        db.AddRange(category, transaction);
        await db.SaveChangesAsync();

        var batch = await service.CreateAsync("gider", null, null, "giderler.csv");
        await service.AddRowAsync(
            batch,
            2,
            new { Description = "EFT MASRAFI" },
            ImportBatchService.BuildNormalizedKey("rollback", transaction.Id),
            ImportRowStatus.Committed,
            createdEntityName: nameof(LedgerTransaction),
            createdEntityId: transaction.Id);
        await service.CommitAsync(batch);

        await service.RollbackAsync(batch.Id);

        var rolledBackBatch = await db.ImportBatches.Include(x => x.Rows).SingleAsync(x => x.Id == batch.Id);
        var hiddenTransaction = await db.LedgerTransactions
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == transaction.Id);

        Assert.Equal(ImportBatchStatus.RolledBack, rolledBackBatch.Status);
        Assert.All(rolledBackBatch.Rows, row => Assert.Equal(ImportRowStatus.RolledBack, row.Status));
        Assert.True(hiddenTransaction.IsDeleted);
        Assert.Empty(await db.LedgerTransactions.ToListAsync());
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ImportBatchService CreateService(ApplicationDbContext db)
    {
        return new ImportBatchService(db, new HttpContextAccessor());
    }

    private static DateTime Utc(int year, int month, int day)
    {
        return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    }
}
