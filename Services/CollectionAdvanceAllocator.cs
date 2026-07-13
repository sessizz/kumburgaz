using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public static class CollectionAdvanceAllocator
{
    public static async Task ApplyAsync(ApplicationDbContext db)
    {
        var collections = await db.Collections
            .Include(x => x.Allocations)
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Id)
            .ToListAsync();

        // Devreden borcu (devir) olan daireler icin, henuz tahsis edilmemis tahsilat once devir borcuna ayrilir.
        var devirRemaining = await db.Units
            .Where(x => x.OpeningBalance < 0m)
            .ToDictionaryAsync(x => x.Id, x => -x.OpeningBalance);

        foreach (var collection in collections)
        {
            var credit = collection.Amount - collection.Allocations.Sum(x => x.AppliedAmount);
            if (credit <= 0)
            {
                continue;
            }

            if (devirRemaining.TryGetValue(collection.UnitId, out var devirLeft) && devirLeft > 0)
            {
                var reserved = Math.Min(credit, devirLeft);
                devirRemaining[collection.UnitId] = devirLeft - reserved;
                credit -= reserved;
            }

            if (credit <= 0)
            {
                continue;
            }

            var installments = await db.DuesInstallments
                .Where(x => x.BillingGroupId == collection.BillingGroupId
                            && x.RemainingAmount > 0
                            && (x.UnitId == collection.UnitId || x.UnitId == null))
                .OrderBy(x => x.Period)
                .ThenBy(x => x.DueDate)
                .ThenBy(x => x.Id)
                .ToListAsync();

            foreach (var installment in installments)
            {
                if (credit <= 0)
                {
                    break;
                }

                var applied = Math.Min(credit, installment.RemainingAmount);
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

                credit -= applied;
            }
        }

        await db.SaveChangesAsync();
    }
}
