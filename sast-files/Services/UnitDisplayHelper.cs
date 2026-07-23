using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Services;

public static class UnitDisplayHelper
{
    public static string Display(Unit? unit)
    {
        if (unit is null)
        {
            return "-";
        }

        var baseName = unit.Block is null ? unit.UnitNo : $"{unit.Block.Name}-{unit.UnitNo}";
        if (!unit.IsCombined)
        {
            return baseName;
        }

        var members = unit.CombinedUnitMembers
            .Where(x => x.ComponentUnit?.Block is not null)
            .Select(x => $"{x.ComponentUnit!.Block!.Name}-{x.ComponentUnit.UnitNo}")
            .OrderBy(x => x)
            .ToList();

        return members.Count == 0
            ? $"{baseName} (Birleşik)"
            : $"{baseName} ({string.Join(" + ", members)})";
    }
}
