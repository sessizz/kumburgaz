using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public class DuesGenerationService(ApplicationDbContext db) : IDuesGenerationService
{
    public async Task<List<DuesGenerationPreviewItem>> PreviewAsync(string period)
    {
        ValidatePeriod(period);
        var periodKey = PeriodHelper.ToKey(period);

        var groups = await db.BillingGroups
            .AsNoTracking()
            .Include(x => x.DuesType)
            .Include(x => x.Units)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Where(x => x.Active)
            .ToListAsync();

        var filtered = groups
            .Where(x => PeriodHelper.ToKey(x.EffectiveStartPeriod) <= periodKey &&
                        (x.EffectiveEndPeriod is null || PeriodHelper.ToKey(x.EffectiveEndPeriod) >= periodKey))
            .Select(x => new DuesGenerationPreviewItem
            {
                BillingGroupId = x.Id,
                BillingGroupName = x.Name,
                DuesTypeName = x.DuesType?.Name ?? "-",
                Amount = x.DuesType?.Amount ?? 0m,
                UnitsText = string.Join(", ", x.Units
                    .Select(u => $"{u.Unit?.Block?.Name}-{u.Unit?.UnitNo}")
                    .OrderBy(v => v))
            })
            .OrderBy(x => x.BillingGroupName)
            .ToList();

        return filtered;
    }

    public async Task GenerateForPeriodAsync(string period, DateTime dueDate)
    {
        var preview = await PreviewAsync(period);

        foreach (var item in preview)
        {
            var exists = await db.DuesInstallments.AnyAsync(x => x.BillingGroupId == item.BillingGroupId && x.Period == period);
            if (exists)
            {
                continue;
            }

            db.DuesInstallments.Add(new DuesInstallment
            {
                BillingGroupId = item.BillingGroupId,
                Period = period,
                DueDate = dueDate,
                Amount = item.Amount,
                RemainingAmount = item.Amount,
                Status = InstallmentStatus.Open
            });
        }

        await db.SaveChangesAsync();
    }

    private static void ValidatePeriod(string period)
    {
        if (!PeriodHelper.IsValid(period))
        {
            throw new InvalidOperationException("Donem formati YYYY-YYYY olmali ve ikinci yil ilk yilin bir sonrasi olmali.");
        }
    }
}
