using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public class UnitLedgerService(ApplicationDbContext db)
{
    public async Task<UnitLedgerResult?> BuildAsync(int unitId)
    {
        var unit = await db.Units
            .AsNoTracking()
            .Include(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == unitId);

        if (unit is null)
        {
            return null;
        }

        var entries = new List<UnitLedgerEntry>();

        if (unit.OpeningBalance != 0m && unit.OpeningBalanceDate.HasValue)
        {
            entries.Add(new UnitLedgerEntry
            {
                Kind = UnitLedgerEntryKind.OpeningBalance,
                Date = unit.OpeningBalanceDate.Value,
                Description = unit.OpeningBalance > 0m
                    ? "Devir Bakiyesi (Alacak)"
                    : "Devir Bakiyesi (Devreden Borç)",
                Amount = -unit.OpeningBalance
            });
        }

        var installments = await db.DuesInstallments
            .AsNoTracking()
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.DuesType)
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.Units)
            .Where(x => x.UnitId == unitId
                        || (x.UnitId == null && x.BillingGroup!.Units.Any(u => u.UnitId == unitId)))
            .ToListAsync();

        foreach (var installment in installments)
        {
            entries.Add(new UnitLedgerEntry
            {
                Kind = UnitLedgerEntryKind.DuesAccrual,
                Date = installment.AccrualDate,
                Description = $"{installment.Period} - {installment.BillingGroup?.DuesType?.Name ?? "Aidat"}",
                Amount = installment.Amount,
                SourceId = installment.Id
            });
        }

        var installmentIds = installments.Select(x => x.Id).ToHashSet();
        var collectionIdsFromAllocations = await db.CollectionAllocations
            .AsNoTracking()
            .Where(x => installmentIds.Contains(x.DuesInstallmentId))
            .Select(x => x.CollectionId)
            .Distinct()
            .ToListAsync();

        var collections = await db.Collections
            .AsNoTracking()
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.DuesType)
            .Where(x => x.UnitId == unitId || collectionIdsFromAllocations.Contains(x.Id))
            .ToListAsync();

        foreach (var collection in collections)
        {
            entries.Add(new UnitLedgerEntry
            {
                Kind = UnitLedgerEntryKind.Collection,
                Date = collection.Date,
                Description = $"{collection.BillingGroup?.DuesType?.Name ?? "Aidat"} Tahsilatı",
                Amount = -collection.Amount,
                SourceId = collection.Id
            });
        }

        var ordered = entries
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Kind == UnitLedgerEntryKind.OpeningBalance ? 0 : x.Kind == UnitLedgerEntryKind.DuesAccrual ? 1 : 2)
            .ThenBy(x => x.SourceId ?? 0)
            .ToList();

        decimal running = 0m;
        foreach (var entry in ordered)
        {
            running += entry.Amount;
            entry.RunningBalance = running;
        }

        return new UnitLedgerResult
        {
            Unit = unit,
            Entries = ordered,
            Summary = new UnitLedgerSummary
            {
                TotalAccrual = installments.Sum(x => x.Amount),
                TotalCollections = collections.Sum(x => x.Amount),
                OpeningCredit = unit.OpeningBalance > 0m ? unit.OpeningBalance : 0m,
                OpeningDebt = unit.OpeningBalance < 0m ? Math.Abs(unit.OpeningBalance) : 0m,
                NetBalance = running
            }
        };
    }

    public async Task<Dictionary<int, UnitLedgerSummary>> BuildSummariesAsync(IEnumerable<int> unitIds)
    {
        var result = new Dictionary<int, UnitLedgerSummary>();
        foreach (var unitId in unitIds.Distinct())
        {
            var ledger = await BuildAsync(unitId);
            if (ledger is not null)
            {
                result[unitId] = ledger.Summary;
            }
        }

        return result;
    }
}
