using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Services;

public static class BillingGroupDisplayHelper
{
    public static string UnitDisplay(BillingGroup? group)
    {
        if (group is null)
        {
            return "-";
        }

        var units = group.Units
            .Where(x => x.Unit?.Block is not null)
            .Select(x => $"{x.Unit!.Block!.Name}-{x.Unit.UnitNo}")
            .OrderBy(x => x)
            .ToList();

        if (units.Count == 0)
        {
            return group.Name;
        }

        return units.Count == 1
            ? units[0]
            : $"{string.Join(" + ", units)} (Birlesik)";
    }
}
