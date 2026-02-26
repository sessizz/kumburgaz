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
        ValidatePeriod(period);
        dueDate = DateTimeHelper.EnsureUtc(dueDate);
        var periodKey = PeriodHelper.ToKey(period);
        var allActiveGroups = await db.BillingGroups
            .AsNoTracking()
            .Include(x => x.DuesType)
            .Include(x => x.Units)
            .Where(x => x.Active)
            .ToListAsync();

        var groups = allActiveGroups
            .Where(x => PeriodHelper.ToKey(x.EffectiveStartPeriod) <= periodKey &&
                        (x.EffectiveEndPeriod is null || PeriodHelper.ToKey(x.EffectiveEndPeriod) >= periodKey))
            .ToList();

        await NormalizeLegacyNonMergedInstallmentsAsync(period);

        foreach (var group in groups)
        {
            var amount = group.DuesType?.Amount ?? 0m;
            if (group.IsMerged)
            {
                var exists = await db.DuesInstallments.AnyAsync(x =>
                    x.BillingGroupId == group.Id && x.Period == period && x.UnitId == null);
                if (exists)
                {
                    continue;
                }

                db.DuesInstallments.Add(new DuesInstallment
                {
                    BillingGroupId = group.Id,
                    UnitId = null,
                    Period = period,
                    DueDate = dueDate,
                    Amount = amount,
                    RemainingAmount = amount,
                    Status = InstallmentStatus.Open
                });
                continue;
            }

            var unitIds = group.Units.Select(u => u.UnitId).Distinct().ToList();
            foreach (var unitId in unitIds)
            {
                var exists = await db.DuesInstallments.AnyAsync(x =>
                    x.BillingGroupId == group.Id && x.Period == period && x.UnitId == unitId);
                if (exists)
                {
                    continue;
                }

                db.DuesInstallments.Add(new DuesInstallment
                {
                    BillingGroupId = group.Id,
                    UnitId = unitId,
                    Period = period,
                    DueDate = dueDate,
                    Amount = amount,
                    RemainingAmount = amount,
                    Status = InstallmentStatus.Open
                });
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task NormalizeLegacyNonMergedInstallmentsAsync(string period)
    {
        var legacyInstallments = await db.DuesInstallments
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.Units)
            .Include(x => x.Allocations)
            .Where(x => x.Period == period && x.UnitId == null && !x.BillingGroup!.IsMerged)
            .ToListAsync();

        foreach (var installment in legacyInstallments)
        {
            var unitIds = installment.BillingGroup!.Units.Select(x => x.UnitId).Distinct().ToList();
            if (unitIds.Count <= 1)
            {
                installment.UnitId = unitIds.FirstOrDefault();
                continue;
            }

            if (installment.Allocations.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Donem {period} icin {installment.BillingGroup.Name} grubunda tahsilat uygulanmis eski tekil borc bulundu. " +
                    "Bu kaydi once manuel temizleyin.");
            }

            installment.UnitId = unitIds[0];
            for (var i = 1; i < unitIds.Count; i++)
            {
                db.DuesInstallments.Add(new DuesInstallment
                {
                    BillingGroupId = installment.BillingGroupId,
                    UnitId = unitIds[i],
                    Period = installment.Period,
                    DueDate = installment.DueDate,
                    Amount = installment.Amount,
                    RemainingAmount = installment.RemainingAmount,
                    Status = installment.Status
                });
            }
        }
    }

    public async Task DeleteForPeriodAsync(string period)
    {
        ValidatePeriod(period);

        var installmentIds = await db.DuesInstallments
            .Where(x => x.Period == period)
            .Select(x => x.Id)
            .ToListAsync();

        if (installmentIds.Count == 0)
        {
            return;
        }

        var hasAllocation = await db.CollectionAllocations
            .AnyAsync(x => installmentIds.Contains(x.DuesInstallmentId));

        if (hasAllocation)
        {
            throw new InvalidOperationException("Bu donemde tahsilat uygulanmis borclar var. Once ilgili tahsilatlari silin veya duzenleyin.");
        }

        var installments = await db.DuesInstallments
            .Where(x => x.Period == period)
            .ToListAsync();

        db.DuesInstallments.RemoveRange(installments);
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
