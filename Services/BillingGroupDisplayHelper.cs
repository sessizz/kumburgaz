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
            .Where(x => x.Unit is not null)
            .Select(x => UnitDisplayHelper.Display(x.Unit))
            .OrderBy(x => x)
            .ToList();

        if (units.Count == 0)
        {
            return group.Name;
        }

        if (units.Count == 1)
        {
            return units[0];
        }

        return group.IsMerged
            ? $"{string.Join(" + ", units)} (Birleşik)"
            : string.Join(", ", units);
    }
}
