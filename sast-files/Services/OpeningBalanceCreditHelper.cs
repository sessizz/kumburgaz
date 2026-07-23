using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Services;

public static class OpeningBalanceCreditHelper
{
    public static Dictionary<int, decimal> BuildEffectiveRemainingMap(IEnumerable<DuesInstallment> installments)
    {
        var ordered = installments
            .Where(x => x.Id > 0)
            .OrderBy(x => x.UnitId)
            .ThenBy(x => x.AccrualDate)
            .ThenBy(x => x.DueDate)
            .ThenBy(x => x.Id)
            .ToList();

        var effectiveRemaining = ordered.ToDictionary(x => x.Id, x => x.RemainingAmount);

        foreach (var group in ordered
                     .Where(x => x.Unit is { OpeningBalance: > 0m })
                     .GroupBy(x => x.UnitId))
        {
            var credit = group.First().Unit!.OpeningBalance;
            foreach (var installment in group)
            {
                if (credit <= 0)
                {
                    break;
                }

                var reduction = Math.Min(effectiveRemaining[installment.Id], credit);
                effectiveRemaining[installment.Id] -= reduction;
                credit -= reduction;
            }
        }

        return effectiveRemaining;
    }
}
