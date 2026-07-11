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

        // Borcu devir/aidat olarak ayrıştır: tahsis edilmemiş tahsilat önce eski devir borcunu kapatır
        // (devir borcunun taksidi olmadığından ödemesi ancak tahsis edilmemiş olarak görünür).
        var totalDebt = debtorRows.Sum(x => x.RemainingAmount);
        var carriedOverDebt = 0m;
        foreach (var row in debtorRows)
        {
            var carriedGross = Math.Max(0m, -row.OpeningBalance);
            carriedOverDebt += Math.Min(Math.Max(0m, carriedGross - row.UnallocatedCredit), row.RemainingAmount);
        }
        var currentPeriodDebt = totalDebt - carriedOverDebt;

        return new DuesDebtSummary
        {
            DebtorCount = debtorRows.Count,
            CreditorCount = creditorRows.Count,
            ClearCount = clearRows.Count,
            TotalDebt = totalDebt,
            TotalCredit = creditorRows.Sum(x => Math.Abs(x.RemainingAmount)),
            TotalAccrual = list.Sum(x => x.Amount),
            CarriedOverDebt = carriedOverDebt,
            CurrentPeriodDebt = currentPeriodDebt
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
    public decimal CarriedOverDebt { get; set; }
    public decimal CurrentPeriodDebt { get; set; }
}
