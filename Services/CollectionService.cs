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
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .ToListAsync();
    }

    public async Task CreateAsync(CollectionCreateViewModel model)
    {
        if (model.Amount <= 0)
        {
            throw new InvalidOperationException("Tahsilat tutari sifirdan buyuk olmalidir.");
        }

        var collection = new Collection
        {
            BillingGroupId = model.BillingGroupId,
            Date = model.Date,
            Amount = model.Amount,
            PaymentChannel = model.PaymentChannel,
            ReferenceNo = model.ReferenceNo,
            Note = model.Note
        };

        db.Collections.Add(collection);
        await db.SaveChangesAsync();

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
    }
}
