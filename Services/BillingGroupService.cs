using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public class BillingGroupService(ApplicationDbContext db) : IBillingGroupService
{
    public Task<List<BillingGroup>> GetAllAsync()
    {
        return db.BillingGroups
            .AsNoTracking()
            .Include(x => x.DuesType)
            .Include(x => x.Units)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .OrderBy(x => x.Name)
            .ToListAsync();
    }

    public Task<BillingGroup?> GetByIdAsync(int id)
    {
        return db.BillingGroups
            .Include(x => x.DuesType)
            .Include(x => x.Units)
            .ThenInclude(x => x.Unit)
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task CreateOrUpdateAsync(BillingGroupFormViewModel model)
    {
        if (!PeriodHelper.IsValid(model.EffectiveStartPeriod) ||
            (!string.IsNullOrWhiteSpace(model.EffectiveEndPeriod) && !PeriodHelper.IsValid(model.EffectiveEndPeriod)))
        {
            throw new InvalidOperationException("Donem formati YYYY-YYYY olmali ve ikinci yil ilk yilin bir sonrasi olmali.");
        }

        if (model.SelectedUnitIds.Count == 0)
        {
            throw new InvalidOperationException("En az bir daire secilmelidir.");
        }

        var selectedUnitIds = model.SelectedUnitIds.Distinct().ToList();

        BillingGroup group;
        if (model.Id.HasValue)
        {
            group = await db.BillingGroups.Include(x => x.Units).FirstAsync(x => x.Id == model.Id.Value);
            group.Name = model.Name.Trim();
            group.DuesTypeId = model.DuesTypeId;
            group.EffectiveStartPeriod = model.EffectiveStartPeriod;
            group.EffectiveEndPeriod = string.IsNullOrWhiteSpace(model.EffectiveEndPeriod) ? null : model.EffectiveEndPeriod;
            group.Active = model.Active;
            group.IsMerged = model.MergeUnits;

            db.BillingGroupUnits.RemoveRange(group.Units);
        }
        else
        {
            group = new BillingGroup
            {
                Name = model.Name.Trim(),
                DuesTypeId = model.DuesTypeId,
                EffectiveStartPeriod = model.EffectiveStartPeriod,
                EffectiveEndPeriod = string.IsNullOrWhiteSpace(model.EffectiveEndPeriod) ? null : model.EffectiveEndPeriod,
                Active = model.Active,
                IsMerged = model.MergeUnits
            };
            db.BillingGroups.Add(group);
            await db.SaveChangesAsync();
        }

        foreach (var unitId in selectedUnitIds)
        {
            db.BillingGroupUnits.Add(new BillingGroupUnit
            {
                BillingGroupId = group.Id,
                UnitId = unitId,
                StartPeriod = model.EffectiveStartPeriod,
                EndPeriod = group.EffectiveEndPeriod
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var group = await db.BillingGroups.FindAsync(id);
        if (group is null)
        {
            return;
        }

        group.Active = false;
        await db.SaveChangesAsync();
    }
}
