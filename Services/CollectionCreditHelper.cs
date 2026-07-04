using Kumburgaz.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public static class CollectionCreditHelper
{
    public static async Task<Dictionary<int, decimal>> BuildUnitCreditMapAsync(
        ApplicationDbContext db,
        int? blockId = null,
        int? billingGroupId = null,
        int? duesTypeId = null)
    {
        return await db.Collections
            .AsNoTracking()
            .Where(x => blockId == null || x.Unit!.BlockId == blockId)
            .Where(x => billingGroupId == null || x.BillingGroupId == billingGroupId)
            .Where(x => duesTypeId == null || x.BillingGroup!.DuesTypeId == duesTypeId)
            .Select(x => new
            {
                x.UnitId,
                Credit = x.Amount - x.Allocations.Sum(a => (decimal?)a.AppliedAmount).GetValueOrDefault()
            })
            .Where(x => x.Credit > 0)
            .GroupBy(x => x.UnitId)
            .Select(x => new
            {
                UnitId = x.Key,
                Credit = x.Sum(c => c.Credit)
            })
            .ToDictionaryAsync(x => x.UnitId, x => x.Credit);
    }
}
