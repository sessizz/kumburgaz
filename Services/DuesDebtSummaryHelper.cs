using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Services;

public static class DuesDebtSummaryHelper
{
    public static DuesDebtSummary Build(IEnumerable<DuesDebtReportRow> rows)
    {
        var list = rows.ToList();
        var debtorRows = list.Where(x => x.RemainingAmount > 0).ToList();
        var creditorRows = list.Where(x => x.RemainingAmount < 0).ToList();
        var clearRows = list.Where(x => x.RemainingAmount == 0).ToList();

        return new DuesDebtSummary
        {
            DebtorCount = debtorRows.Count,
            CreditorCount = creditorRows.Count,
            ClearCount = clearRows.Count,
            TotalDebt = debtorRows.Sum(x => x.RemainingAmount),
            TotalCredit = creditorRows.Sum(x => Math.Abs(x.RemainingAmount)),
            TotalAccrual = list.Sum(x => x.Amount)
        };
    }
}

public class DuesDebtSummary
{
    public int DebtorCount { get; set; }
    public int CreditorCount { get; set; }
    public int ClearCount { get; set; }
    public decimal TotalDebt { get; set; }
    public decimal TotalCredit { get; set; }
    public decimal NetBalance => TotalDebt - TotalCredit;
    public decimal TotalAccrual { get; set; }
}
