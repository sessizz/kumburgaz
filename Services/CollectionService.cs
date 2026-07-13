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
            .ThenInclude(x => x!.Units)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.Units)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.CombinedUnitMembers)
            .ThenInclude(x => x.ComponentUnit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.CashBox)
            .Include(x => x.BankAccount)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .ToListAsync();
    }

    public Task<Collection?> GetByIdAsync(int id)
    {
        return db.Collections
            .AsNoTracking()
            .Include(x => x.Allocations)
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<int> CreateAsync(CollectionCreateViewModel model)
    {
        return await SaveCollectionAndReallocateAsync(null, model);
    }

    public async Task UpdateAsync(int id, CollectionCreateViewModel model)
    {
        await SaveCollectionAndReallocateAsync(id, model);
    }

    public async Task DeleteAsync(int id)
    {
        var useTransaction = !db.Database.ProviderName!.Contains("InMemory", StringComparison.OrdinalIgnoreCase)
            && db.Database.CurrentTransaction is null;
        await using var tx = useTransaction ? await db.Database.BeginTransactionAsync() : null;

        var collection = await db.Collections
            .Include(x => x.Allocations)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (collection is null)
        {
            return;
        }

        var installmentIds = collection.Allocations.Select(x => x.DuesInstallmentId).ToList();
        var installments = await db.DuesInstallments
            .Where(x => installmentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);

        foreach (var allocation in collection.Allocations)
        {
            var installment = installments[allocation.DuesInstallmentId];
            installment.RemainingAmount += allocation.AppliedAmount;
            installment.Status = installment.RemainingAmount >= installment.Amount
                ? InstallmentStatus.Open
                : InstallmentStatus.PartiallyPaid;
        }

        db.CollectionAllocations.RemoveRange(collection.Allocations);
        db.Collections.Remove(collection);
        await db.SaveChangesAsync();

        if (tx is not null)
        {
            await tx.CommitAsync();
        }
    }

    private async Task<int> SaveCollectionAndReallocateAsync(int? collectionId, CollectionCreateViewModel model)
    {
        if (model.Amount <= 0)
        {
            throw new InvalidOperationException("Tahsilat tutari sifirdan buyuk olmalidir.");
        }

        if (model.IsReceipt && string.IsNullOrWhiteSpace(model.ReferenceNo))
        {
            throw new InvalidOperationException("Makbuz için makbuz no giriniz.");
        }

        var targetInstallment = model.DuesInstallmentId.HasValue
            ? await db.DuesInstallments
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == model.DuesInstallmentId.Value)
            : null;

        if (model.DuesInstallmentId.HasValue && targetInstallment is null)
        {
            throw new InvalidOperationException("Secilen aidat borcu bulunamadi.");
        }

        var billingGroupId = targetInstallment?.BillingGroupId ?? model.BillingGroupId;
        if (billingGroupId <= 0)
        {
            throw new InvalidOperationException("Tahsilat icin aidat borcu seciniz.");
        }

        var hasAccount = FinancialAccountHelper.TryParse(model.AccountKey, out var paymentChannel, out var cashBoxId, out var bankAccountId);
        if (!hasAccount)
        {
            paymentChannel = model.PaymentChannel;
        }

        var representativeUnitId = targetInstallment?.UnitId ?? model.PreferredUnitId ?? await ResolveRepresentativeUnitIdAsync(billingGroupId);
        var utcDate = DateTimeHelper.EnsureUtc(model.Date);

        var useTransaction = !db.Database.ProviderName!.Contains("InMemory", StringComparison.OrdinalIgnoreCase)
            && db.Database.CurrentTransaction is null;
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

            collection.BillingGroupId = billingGroupId;
            collection.UnitId = representativeUnitId;
            collection.Date = utcDate;
            collection.Amount = model.Amount;
            collection.PaymentChannel = paymentChannel;
            collection.CashBoxId = cashBoxId;
            collection.BankAccountId = bankAccountId;
            collection.ReferenceNo = model.ReferenceNo;
            collection.IsReceipt = model.IsReceipt;
            collection.Note = model.Note;
        }
        else
        {
            collection = new Collection
            {
                BillingGroupId = billingGroupId,
                UnitId = representativeUnitId,
                Date = utcDate,
                Amount = model.Amount,
                PaymentChannel = paymentChannel,
                CashBoxId = cashBoxId,
                BankAccountId = bankAccountId,
                ReferenceNo = model.ReferenceNo,
                IsReceipt = model.IsReceipt,
                Note = model.Note
            };

            db.Collections.Add(collection);
            await db.SaveChangesAsync();
        }

        var openInstallmentsQuery = db.DuesInstallments
            .Where(x => x.BillingGroupId == billingGroupId && x.RemainingAmount > 0);

        if (targetInstallment?.UnitId is not null)
        {
            var unitId = targetInstallment.UnitId.Value;
            openInstallmentsQuery = openInstallmentsQuery.Where(x => x.UnitId == unitId);
        }

        var openInstallments = await openInstallmentsQuery
            .OrderBy(x => x.Period)
            .ThenBy(x => x.DueDate)
            .ThenBy(x => x.Id)
            .ToListAsync();

        var remaining = model.Amount;

        // Belirli bir taksit secilmediyse (genel/serbest tahsilat), daireye ait devreden borcu
        // once kapatir; kalan varsa aidat taksitlerine dagitilir.
        if (targetInstallment is null)
        {
            var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(x => x.Id == representativeUnitId);
            if (unit is not null && unit.OpeningBalance < 0m)
            {
                var totalUnallocatedForUnit = await db.Collections
                    .Where(x => x.UnitId == representativeUnitId)
                    .Select(x => x.Amount - x.Allocations.Sum(a => (decimal?)a.AppliedAmount).GetValueOrDefault())
                    .SumAsync();
                var otherUnallocated = Math.Max(0m, totalUnallocatedForUnit - remaining);
                var devirLeft = Math.Max(0m, -unit.OpeningBalance - otherUnallocated);
                remaining -= Math.Min(remaining, devirLeft);
            }
        }

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

        return collection.Id;
    }

    private async Task<int> ResolveRepresentativeUnitIdAsync(int billingGroupId)
    {
        var unitId = await db.BillingGroupUnits
            .Where(x => x.BillingGroupId == billingGroupId && x.Unit!.Active)
            .OrderBy(x => x.Unit!.Block!.Name)
            .ThenBy(x => x.Unit!.UnitNo)
            .Select(x => x.UnitId)
            .FirstOrDefaultAsync();

        if (unitId == 0)
        {
            throw new InvalidOperationException("Secilen aidatlandirma grubunda daire kaydi bulunamadi.");
        }

        return unitId;
    }
}
