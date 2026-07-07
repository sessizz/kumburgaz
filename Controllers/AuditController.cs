using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize(Policy = AppPolicies.SystemAdmin)]
public class AuditController(
    ApplicationDbContext db,
    ImportBatchService importBatchService,
    ConsistencyCheckService consistencyCheckService) : Controller
{
    public async Task<IActionResult> Index()
    {
        return View(new AuditIndexViewModel
        {
            AuditLogs = await db.AuditLogs
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Take(200)
                .ToListAsync(),
            ImportBatches = await db.ImportBatches
                .AsNoTracking()
                .Include(x => x.Rows)
                .OrderByDescending(x => x.CreatedAt)
                .Take(50)
                .ToListAsync(),
            ConsistencyIssues = await db.ConsistencyCheckResults
                .AsNoTracking()
                .Where(x => !x.Resolved)
                .OrderByDescending(x => x.CreatedAt)
                .Take(50)
                .ToListAsync()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(string entityName, string entityId)
    {
        if (!int.TryParse(entityId, out var id))
        {
            TempData["ActionError"] = "Geri alınacak kayıt bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        var restored = entityName switch
        {
            nameof(Block) => await RestoreEntityAsync<Block>(id),
            nameof(Unit) => await RestoreEntityAsync<Unit>(id),
            nameof(Account) => await RestoreEntityAsync<Account>(id),
            nameof(UnitAccount) => await RestoreEntityAsync<UnitAccount>(id),
            nameof(CombinedUnitMember) => await RestoreEntityAsync<CombinedUnitMember>(id),
            nameof(DuesType) => await RestoreEntityAsync<DuesType>(id),
            nameof(BillingGroup) => await RestoreEntityAsync<BillingGroup>(id),
            nameof(BillingGroupUnit) => await RestoreEntityAsync<BillingGroupUnit>(id),
            nameof(DuesInstallment) => await RestoreEntityAsync<DuesInstallment>(id),
            nameof(Collection) => await RestoreEntityAsync<Collection>(id),
            nameof(CollectionAllocation) => await RestoreEntityAsync<CollectionAllocation>(id),
            nameof(IncomeExpenseCategory) => await RestoreEntityAsync<IncomeExpenseCategory>(id),
            nameof(LedgerTransaction) => await RestoreEntityAsync<LedgerTransaction>(id),
            nameof(BankAccount) => await RestoreEntityAsync<BankAccount>(id),
            nameof(CashBox) => await RestoreEntityAsync<CashBox>(id),
            _ => false
        };

        TempData[restored ? "ActionSuccess" : "ActionError"] = restored
            ? "Kayıt geri alındı."
            : "Geri alınacak kayıt bulunamadı.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RollbackImport(int id)
    {
        await importBatchService.RollbackAsync(id);
        TempData["ActionSuccess"] = "Import batch geri alındı.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunConsistencyCheck()
    {
        var count = await consistencyCheckService.RunAsync();
        TempData["ActionSuccess"] = count == 0
            ? "Tutarlılık kontrolü tamamlandı; sorun bulunmadı."
            : $"Tutarlılık kontrolü tamamlandı; {count} uyarı bulundu.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> RestoreEntityAsync<TEntity>(int id)
        where TEntity : class, ISoftDeletable
    {
        var entity = await db.Set<TEntity>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => EF.Property<int>(x, "Id") == id);

        if (entity is null || !entity.IsDeleted)
        {
            return false;
        }

        entity.IsDeleted = false;
        entity.DeletedAt = null;
        entity.DeletedByUserId = null;
        entity.DeletedByUserName = null;
        await db.SaveChangesAsync();
        return true;
    }
}
