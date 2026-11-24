using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public class UnitLedgerService(ApplicationDbContext db, IDuesLedgerRowService ledgerService)
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
            .Where(x => x.DuesInstallmentId.HasValue && installmentIds.Contains(x.DuesInstallmentId.Value))
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

        var openingDebt = 0m;
        if (unit.OpeningBalance < 0m)
        {
            // Devir borcuna fiilen tahsis edilmiş (kalıcı) tutar düşülür - artık tahmine dayanmaz.
            var openingDebtRemaining = await ledgerService.GetOpeningDebtRemainingByUnitAsync([unitId]);
            openingDebt = openingDebtRemaining.GetValueOrDefault(unitId, Math.Abs(unit.OpeningBalance));
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
                OpeningDebt = openingDebt,
                NetBalance = running
            }
        };
    }

    /// <summary>
    /// <see cref="BuildAsync"/> ile aynı özetleri, daire başına ayrı sorgu çalıştırmadan toplu hesaplar
    /// (Daireler listesi gibi ekranlar yüzlerce daire için çağırır).
    /// </summary>
    public async Task<Dictionary<int, UnitLedgerSummary>> BuildSummariesAsync(IEnumerable<int> unitIds)
    {
        var ids = unitIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<int, UnitLedgerSummary>();
        }

        var units = await db.Units
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.OpeningBalance, x.OpeningBalanceDate })
            .ToListAsync();
        if (units.Count == 0)
        {
            return new Dictionary<int, UnitLedgerSummary>();
        }

        var foundIds = units.Select(x => x.Id).ToList();

        // BuildAsync taksit ve tahsilatları BillingGroup ile birlikte (Include) okur; silinmiş grubun
        // kayıtları orada düşer. Aynı sonuç için yalnız görünen grupların kayıtları sayılır.
        var visibleGroupIds = (await db.BillingGroups.AsNoTracking().Select(x => x.Id).ToListAsync()).ToHashSet();

        var memberships = await db.BillingGroupUnits
            .AsNoTracking()
            .Where(x => foundIds.Contains(x.UnitId))
            .Select(x => new { x.BillingGroupId, x.UnitId })
            .ToListAsync();
        var membershipGroupIds = memberships.Select(x => x.BillingGroupId).Distinct().ToList();

        var installments = (await db.DuesInstallments
                .AsNoTracking()
                .Where(x => (x.UnitId != null && foundIds.Contains(x.UnitId.Value))
                            || (x.UnitId == null && membershipGroupIds.Contains(x.BillingGroupId)))
                .Select(x => new { x.Id, x.UnitId, x.BillingGroupId, x.Amount })
                .ToListAsync())
            .Where(x => visibleGroupIds.Contains(x.BillingGroupId))
            .ToList();

        var installmentIds = installments.Select(x => x.Id).ToList();
        var allocations = await db.CollectionAllocations
            .AsNoTracking()
            .Where(x => x.DuesInstallmentId.HasValue && installmentIds.Contains(x.DuesInstallmentId.Value))
            .Select(x => new { InstallmentId = x.DuesInstallmentId!.Value, x.CollectionId })
            .ToListAsync();
        var allocatedCollectionIds = allocations.Select(x => x.CollectionId).Distinct().ToList();

        var collections = (await db.Collections
                .AsNoTracking()
                .Where(x => foundIds.Contains(x.UnitId) || allocatedCollectionIds.Contains(x.Id))
                .Select(x => new { x.Id, x.UnitId, x.BillingGroupId, x.Amount })
                .ToListAsync())
            .Where(x => visibleGroupIds.Contains(x.BillingGroupId))
            .ToDictionary(x => x.Id);

        var groupsByUnit = memberships.ToLookup(x => x.UnitId, x => x.BillingGroupId);
        var directInstallmentsByUnit = installments.Where(x => x.UnitId.HasValue).ToLookup(x => x.UnitId!.Value);
        var groupInstallmentsByGroup = installments.Where(x => !x.UnitId.HasValue).ToLookup(x => x.BillingGroupId);
        var collectionIdsByInstallment = allocations.ToLookup(x => x.InstallmentId, x => x.CollectionId);
        var collectionsByUnit = collections.Values.ToLookup(x => x.UnitId);

        var openingDebtRemaining = await ledgerService.GetOpeningDebtRemainingByUnitAsync(
            units.Where(x => x.OpeningBalance < 0m).Select(x => x.Id));

        var result = new Dictionary<int, UnitLedgerSummary>();
        foreach (var unit in units)
        {
            var unitInstallments = directInstallmentsByUnit[unit.Id]
                .Concat(groupsByUnit[unit.Id].Distinct().SelectMany(groupId => groupInstallmentsByGroup[groupId]))
                .DistinctBy(x => x.Id)
                .ToList();

            var unitCollectionIds = collectionsByUnit[unit.Id].Select(x => x.Id)
                .Concat(unitInstallments.SelectMany(x => collectionIdsByInstallment[x.Id]))
                .Distinct();
            var totalCollections = unitCollectionIds
                .Where(collections.ContainsKey)
                .Sum(id => collections[id].Amount);

            var totalAccrual = unitInstallments.Sum(x => x.Amount);
            var opening = unit.OpeningBalance != 0m && unit.OpeningBalanceDate.HasValue ? -unit.OpeningBalance : 0m;

            result[unit.Id] = new UnitLedgerSummary
            {
                TotalAccrual = totalAccrual,
                TotalCollections = totalCollections,
                OpeningCredit = unit.OpeningBalance > 0m ? unit.OpeningBalance : 0m,
                OpeningDebt = unit.OpeningBalance < 0m
                    ? openingDebtRemaining.GetValueOrDefault(unit.Id, Math.Abs(unit.OpeningBalance))
                    : 0m,
                NetBalance = opening + totalAccrual - totalCollections
            };
        }

        return result;
    }
}
