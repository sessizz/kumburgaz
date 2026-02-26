using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public class CollectionService(ApplicationDbContext db) : ICollectionService
{
    public Task<List<Collection>> GetAllAsync()
    {
        return db.Collections
            .AsNoTracking()
            .Include(x => x.BillingGroup)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .ToListAsync();
    }

    public Task<Collection?> GetByIdAsync(int id)
    {
        return db.Collections
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task CreateAsync(CollectionCreateViewModel model)
    {
        await ValidateSelectionAsync(model.UnitId, model.BillingGroupId);
        await SaveCollectionAndReallocateAsync(null, model);
    }

    public async Task UpdateAsync(int id, CollectionCreateViewModel model)
    {
        await ValidateSelectionAsync(model.UnitId, model.BillingGroupId);
        await SaveCollectionAndReallocateAsync(id, model);
    }

    private async Task SaveCollectionAndReallocateAsync(int? collectionId, CollectionCreateViewModel model)
    {
        if (model.Amount <= 0)
        {
            throw new InvalidOperationException("Tahsilat tutari sifirdan buyuk olmalidir.");
        }

        var utcDate = DateTimeHelper.EnsureUtc(model.Date);

        var useTransaction = !db.Database.ProviderName!.Contains("InMemory", StringComparison.OrdinalIgnoreCase);
        await using var tx = useTransaction ? await db.Database.BeginTransactionAsync() : null;
        Collection collection;

        if (collectionId.HasValue)
        {
            collection = await db.Collections
                .Include(x => x.Allocations)
                .FirstOrDefaultAsync(x => x.Id == collectionId.Value)
                ?? throw new InvalidOperationException("Tahsilat kaydi bulunamadi.");

            // Eski mahsuplari geri al.
            var installmentIds = collection.Allocations.Select(x => x.DuesInstallmentId).ToList();
            var installmentsToRollback = await db.DuesInstallments
                .Where(x => installmentIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id);

            foreach (var allocation in collection.Allocations)
            {
                var installment = installmentsToRollback[allocation.DuesInstallmentId];
                installment.RemainingAmount += allocation.AppliedAmount;
                installment.Status = installment.RemainingAmount >= installment.Amount
                    ? InstallmentStatus.Open
                    : InstallmentStatus.PartiallyPaid;
            }

            db.CollectionAllocations.RemoveRange(collection.Allocations);

            collection.BillingGroupId = model.BillingGroupId;
            collection.UnitId = model.UnitId;
            collection.Date = utcDate;
            collection.Amount = model.Amount;
            collection.PaymentChannel = model.PaymentChannel;
            collection.ReferenceNo = model.ReferenceNo;
            collection.Note = model.Note;
        }
        else
        {
            collection = new Collection
            {
                BillingGroupId = model.BillingGroupId,
                UnitId = model.UnitId,
                Date = utcDate,
                Amount = model.Amount,
                PaymentChannel = model.PaymentChannel,
                ReferenceNo = model.ReferenceNo,
                Note = model.Note
            };

            db.Collections.Add(collection);
            await db.SaveChangesAsync();
        }

        var openInstallments = await db.DuesInstallments
            .Where(x => x.BillingGroupId == model.BillingGroupId && x.RemainingAmount > 0)
            .OrderBy(x => x.Period)
            .ThenBy(x => x.DueDate)
            .ToListAsync();

        var remaining = model.Amount;
        foreach (var installment in openInstallments)
        {
            if (remaining <= 0)
            {
                break;
            }

            var applied = Math.Min(remaining, installment.RemainingAmount);
            installment.RemainingAmount -= applied;
            installment.Status = installment.RemainingAmount <= 0
                ? InstallmentStatus.Paid
                : installment.RemainingAmount < installment.Amount
                    ? InstallmentStatus.PartiallyPaid
                    : InstallmentStatus.Open;

            db.CollectionAllocations.Add(new CollectionAllocation
            {
                CollectionId = collection.Id,
                DuesInstallmentId = installment.Id,
                AppliedAmount = applied
            });

            remaining -= applied;
        }

        await db.SaveChangesAsync();
        if (tx is not null)
        {
            await tx.CommitAsync();
        }
    }

    private async Task ValidateSelectionAsync(int unitId, int billingGroupId)
    {
        var inGroup = await db.BillingGroupUnits
            .AnyAsync(x => x.UnitId == unitId && x.BillingGroupId == billingGroupId);

        if (!inGroup)
        {
            throw new InvalidOperationException("Secilen daire, secilen aidatlandirma grubuna ait degil.");
        }
    }
}
