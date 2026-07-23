using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public class CollectionAllocationRepairService(ApplicationDbContext db)
{
    public async Task<CollectionAllocationRepairResult> RepairCollectionAsync(int collectionId)
    {
        var collection = await db.Collections
            .Include(x => x.Allocations)
            .FirstOrDefaultAsync(x => x.Id == collectionId)
            ?? throw new InvalidOperationException("Tahsilat bulunamadı.");

        var useTransaction = !db.Database.ProviderName!.Contains("InMemory", StringComparison.OrdinalIgnoreCase)
            && db.Database.CurrentTransaction is null;
        await using var tx = useTransaction ? await db.Database.BeginTransactionAsync() : null;

        var result = await RepairLoadedCollectionAsync(collection);
        await db.SaveChangesAsync();

        if (tx is not null)
        {
            await tx.CommitAsync();
        }

        return result;
    }

    public async Task<CollectionAllocationRepairBatchResult> RepairAllAsync()
    {
        var useTransaction = !db.Database.ProviderName!.Contains("InMemory", StringComparison.OrdinalIgnoreCase)
            && db.Database.CurrentTransaction is null;
        await using var tx = useTransaction ? await db.Database.BeginTransactionAsync() : null;

        var installments = await db.DuesInstallments.ToListAsync();
        foreach (var installment in installments)
        {
            installment.RemainingAmount = installment.Amount;
            installment.Status = InstallmentStatus.Open;
        }

        var activeAllocations = await db.CollectionAllocations.ToListAsync();
        db.CollectionAllocations.RemoveRange(activeAllocations);
        await db.SaveChangesAsync();

        var collections = await db.Collections
            .Include(x => x.Allocations)
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Id)
            .ToListAsync();

        var repaired = new List<CollectionAllocationRepairResult>();
        foreach (var collection in collections)
        {
            repaired.Add(await RepairLoadedCollectionAsync(collection, rollbackExistingAllocations: false));
        }

        await db.SaveChangesAsync();

        if (tx is not null)
        {
            await tx.CommitAsync();
        }

        return new CollectionAllocationRepairBatchResult
        {
            CollectionCount = repaired.Count,
            TotalAppliedAmount = repaired.Sum(x => x.NewAllocatedAmount),
            TotalAdvanceAmount = repaired.Sum(x => x.NewAdvanceAmount)
        };
    }

    private async Task<CollectionAllocationRepairResult> RepairLoadedCollectionAsync(
        Collection collection,
        bool rollbackExistingAllocations = true)
    {
        var oldAllocated = collection.Allocations.Sum(x => x.AppliedAmount);

        if (rollbackExistingAllocations && collection.Allocations.Count > 0)
        {
            var allocationIds = collection.Allocations.Select(x => x.DuesInstallmentId).ToList();
            var installmentsToRollback = await db.DuesInstallments
                .Where(x => allocationIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id);

            foreach (var allocation in collection.Allocations)
            {
                if (!installmentsToRollback.TryGetValue(allocation.DuesInstallmentId, out var installment))
                {
                    continue;
                }

                installment.RemainingAmount = Math.Min(installment.Amount, installment.RemainingAmount + allocation.AppliedAmount);
                installment.Status = ResolveInstallmentStatus(installment.Amount, installment.RemainingAmount);
            }

            db.CollectionAllocations.RemoveRange(collection.Allocations);
            collection.Allocations.Clear();
            await db.SaveChangesAsync();
        }

        var remaining = collection.Amount;
        var newAllocated = 0m;
        var affectedInstallmentIds = new List<int>();

        var openInstallments = await db.DuesInstallments
            .Where(x => x.BillingGroupId == collection.BillingGroupId
                        && x.RemainingAmount > 0
                        && (x.UnitId == collection.UnitId || x.UnitId == null))
            .OrderBy(x => x.Period)
            .ThenBy(x => x.DueDate)
            .ThenBy(x => x.Id)
            .ToListAsync();

        foreach (var installment in openInstallments)
        {
            if (remaining <= 0)
            {
                break;
            }

            var applied = Math.Min(remaining, installment.RemainingAmount);
            if (applied <= 0)
            {
                continue;
            }

            installment.RemainingAmount -= applied;
            installment.Status = ResolveInstallmentStatus(installment.Amount, installment.RemainingAmount);

            db.CollectionAllocations.Add(new CollectionAllocation
            {
                CollectionId = collection.Id,
                DuesInstallmentId = installment.Id,
                AppliedAmount = applied
            });

            remaining -= applied;
            newAllocated += applied;
            affectedInstallmentIds.Add(installment.Id);
        }

        return new CollectionAllocationRepairResult
        {
            CollectionId = collection.Id,
            CollectionAmount = collection.Amount,
            OldAllocatedAmount = oldAllocated,
            NewAllocatedAmount = newAllocated,
            OldAdvanceAmount = Math.Max(0m, collection.Amount - oldAllocated),
            NewAdvanceAmount = Math.Max(0m, collection.Amount - newAllocated),
            AffectedInstallmentIds = affectedInstallmentIds
        };
    }

    private static InstallmentStatus ResolveInstallmentStatus(decimal amount, decimal remainingAmount)
    {
        if (remainingAmount <= 0.01m)
        {
            return InstallmentStatus.Paid;
        }

        return remainingAmount < amount - 0.01m
            ? InstallmentStatus.PartiallyPaid
            : InstallmentStatus.Open;
    }
}

public class CollectionAllocationRepairResult
{
    public int CollectionId { get; set; }
    public decimal CollectionAmount { get; set; }
    public decimal OldAllocatedAmount { get; set; }
    public decimal NewAllocatedAmount { get; set; }
    public decimal OldAdvanceAmount { get; set; }
    public decimal NewAdvanceAmount { get; set; }
    public List<int> AffectedInstallmentIds { get; set; } = [];
}

public class CollectionAllocationRepairBatchResult
{
    public int CollectionCount { get; set; }
    public decimal TotalAppliedAmount { get; set; }
    public decimal TotalAdvanceAmount { get; set; }
}
