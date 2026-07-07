using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Services;

/// <summary>
/// Bir daireye ait tüm finansal hareketleri (devir bakiyesi, aidat tahakkukları,
/// tahsilatlar) tarihe göre sıralayarak yürüyen bakiyeli ekstre üretir.
/// </summary>
public class UnitStatementService(UnitLedgerService unitLedgerService)
{
    public async Task<List<StatementEntry>> BuildAsync(int unitId)
    {
        var ledger = await unitLedgerService.BuildAsync(unitId);
        if (ledger is null) return [];

        return ledger.Entries.Select(x => new StatementEntry
        {
            Kind = x.Kind switch
            {
                UnitLedgerEntryKind.OpeningBalance => StatementEntryKind.OpeningBalance,
                UnitLedgerEntryKind.DuesAccrual => StatementEntryKind.Debt,
                _ => StatementEntryKind.Collection
            },
            Date = x.Date,
            Description = x.Description,
            Amount = x.Amount,
            RunningBalance = x.RunningBalance
        }).ToList();
    }
}
