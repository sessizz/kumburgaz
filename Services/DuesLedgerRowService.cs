using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public sealed record UnitCollectionCredit(int UnitId, decimal Amount, DateTime? LastDate);

public interface IDuesLedgerRowService
{
    Task<List<DuesListItemViewModel>> GetInstallmentRowsAsync();
    Task<List<string>> GetAvailablePeriodsAsync();

    /// <summary>
    /// Her dairenin, henüz herhangi bir aidat taksitine tahsis edilmemiş tahsilat fazlasını döner.
    /// <paramref name="unitIds"/> verilirse sorgu sadece o dairelerle sınırlanır.
    /// </summary>
    Task<Dictionary<int, UnitCollectionCredit>> GetUnallocatedCollectionCreditByUnitAsync(IEnumerable<int>? unitIds = null);

    /// <summary>
    /// Devreden borcu (Unit.OpeningBalance &lt; 0) olan her dairenin, o borca fiilen tahsis
    /// edilmiş (CollectionAllocation.UnitId dolu, DuesInstallmentId boş) tutar düşüldükten
    /// sonra kalan devir borcunu döner. Devir borcunun kapanıp kapanmadığının TEK doğru
    /// kaynağı budur - "tahsis edilmemiş kredi" tahminine dayanmaz, kalıcı kayıtlara dayanır.
    /// <paramref name="unitIds"/> verilirse sorgu sadece o dairelerle sınırlanır.
    /// </summary>
    Task<Dictionary<int, decimal>> GetOpeningDebtRemainingByUnitAsync(IEnumerable<int>? unitIds = null);
}

public class DuesLedgerRowService(ApplicationDbContext db) : IDuesLedgerRowService
{
    public async Task<List<DuesListItemViewModel>> GetInstallmentRowsAsync()
    {
        var installments = await db.DuesInstallments
            .AsNoTracking()
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.DuesType)
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.Units)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.CombinedUnitMembers)
            .ThenInclude(x => x.ComponentUnit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Allocations)
            .ThenInclude(x => x.Collection)
            .Include(x => x.ResponsibleAccount)
            .ToListAsync();

        var rows = installments
            .Select(x =>
            {
                var unit = x.Unit;
                var paidDate = x.Allocations
                    .Where(a => a.Collection is not null)
                    .OrderByDescending(a => a.Collection!.Date)
                    .Select(a => (DateTime?)a.Collection!.Date)
                    .FirstOrDefault();
                var isPaid = x.RemainingAmount <= 0;

                return new DuesListItemViewModel
                {
                    Id = x.Id,
                    UnitId = x.UnitId ?? FirstActiveGroupUnit(x.BillingGroup)?.Id,
                    Period = x.Period,
                    BlockName = unit?.Block?.Name ?? FirstActiveGroupUnit(x.BillingGroup)?.Block?.Name ?? "-",
                    UnitNo = unit?.UnitNo ?? FirstActiveGroupUnit(x.BillingGroup)?.UnitNo ?? "-",
                    OwnerName = unit?.OwnerName ?? FirstActiveGroupUnit(x.BillingGroup)?.OwnerName ?? string.Empty,
                    ResponsibleAccountName = x.ResponsibleAccount?.Name ?? string.Empty,
                    UnitDisplay = unit is not null ? UnitDisplayHelper.Display(unit) : BillingGroupDisplayHelper.UnitDisplay(x.BillingGroup),
                    DuesTypeName = x.BillingGroup?.DuesType?.Name ?? "Aidat",
                    AccrualDate = x.AccrualDate,
                    PaymentOrDueDate = isPaid && paidDate.HasValue ? paidDate.Value : x.DueDate,
                    LastPaymentDate = paidDate,
                    IsPaid = isPaid,
                    IsOverdue = !isPaid && x.DueDate.Date < DateTime.Today,
                    Amount = x.Amount,
                    RemainingAmount = x.RemainingAmount
                };
            })
            .ToList();

        // Devir bakiyelerini uygula
        await ApplyOpeningBalancesAsync(rows);

        return rows;
    }

    public async Task<List<string>> GetAvailablePeriodsAsync()
    {
        var periods = await db.DuesInstallments
            .AsNoTracking()
            .Select(x => x.Period)
            .Distinct()
            .ToListAsync();

        var current = PeriodHelper.CurrentFiscalPeriod(DateTime.Today);
        return periods
            .Where(p => PeriodHelper.IsValid(p))
            .Append(current)
            .Distinct()
            .OrderByDescending(PeriodHelper.ToKey)
            .ToList();
    }

    public async Task<Dictionary<int, UnitCollectionCredit>> GetUnallocatedCollectionCreditByUnitAsync(IEnumerable<int>? unitIds = null)
    {
        var query = db.Collections.AsNoTracking().AsQueryable();
        if (unitIds is not null)
        {
            var idList = unitIds as ICollection<int> ?? unitIds.ToList();
            query = query.Where(x => idList.Contains(x.UnitId));
        }

        return await query
            .Select(x => new
            {
                x.UnitId,
                x.Date,
                Credit = x.Amount - x.Allocations.Sum(a => (decimal?)a.AppliedAmount).GetValueOrDefault()
            })
            .Where(x => x.Credit > 0)
            .GroupBy(x => x.UnitId)
            .Select(x => new UnitCollectionCredit(
                x.Key,
                x.Sum(c => c.Credit),
                x.Max(c => (DateTime?)c.Date)))
            .ToDictionaryAsync(x => x.UnitId);
    }

    public async Task<Dictionary<int, decimal>> GetOpeningDebtRemainingByUnitAsync(IEnumerable<int>? unitIds = null)
    {
        var unitsQuery = db.Units.AsNoTracking().Where(x => x.OpeningBalance < 0m);
        if (unitIds is not null)
        {
            var idList = unitIds as ICollection<int> ?? unitIds.ToList();
            unitsQuery = unitsQuery.Where(x => idList.Contains(x.Id));
        }

        var units = await unitsQuery.Select(x => new { x.Id, x.OpeningBalance }).ToListAsync();
        if (units.Count == 0)
        {
            return new Dictionary<int, decimal>();
        }

        var unitIdSet = units.Select(x => x.Id).ToList();
        var appliedToDevir = await db.CollectionAllocations
            .AsNoTracking()
            .Where(x => x.UnitId != null && x.DuesInstallmentId == null && unitIdSet.Contains(x.UnitId!.Value))
            .GroupBy(x => x.UnitId!.Value)
            .Select(g => new { UnitId = g.Key, Applied = g.Sum(x => x.AppliedAmount) })
            .ToDictionaryAsync(x => x.UnitId, x => x.Applied);

        return units.ToDictionary(
            x => x.Id,
            x => Math.Max(0m, -x.OpeningBalance - appliedToDevir.GetValueOrDefault(x.Id)));
    }

    /// <summary>
    /// Her dairenin devir/avans bakiyesini aidat satırlarına yansıtır:
    /// pozitif devir veya tahsis edilmemiş tahsilat → en eski taksitlerin RemainingAmount'unu azaltır,
    /// negatif devir → ek bir "Devir Bakiyesi" satırı eklenir.
    /// </summary>
    private async Task ApplyOpeningBalancesAsync(List<DuesListItemViewModel> rows)
    {
        var collectionCredits = await GetUnallocatedCollectionCreditByUnitAsync();
        var openingDebtRemaining = await GetOpeningDebtRemainingByUnitAsync();

        var creditUnitIds = collectionCredits.Keys.ToHashSet();
        var units = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .Where(x => x.OpeningBalance != 0m || creditUnitIds.Contains(x.Id))
            .ToListAsync();
        if (units.Count == 0) return;

        foreach (var unit in units)
        {
            var unitRows = rows.Where(r => r.UnitId == unit.Id).ToList();
            var collectionCredit = collectionCredits.GetValueOrDefault(unit.Id);
            var credit = collectionCredit?.Amount ?? 0m;

            if (unit.OpeningBalance > 0)
            {
                credit += unit.OpeningBalance;
            }
            else if (unit.OpeningBalance < 0)
            {
                // Devir borcuna fiilen tahsis edilmiş (kalıcı) tutar düşülür. "credit" (tahsis
                // edilmemiş kredi) buradan ayrıca düşülmez - devire ayrılan tutar artık gerçek
                // bir CollectionAllocation olduğu için zaten "tahsis edilmemiş" sayılmıyor
                // (çifte düşüm/çifte sayım olmaz).
                var debt = openingDebtRemaining.GetValueOrDefault(unit.Id, -unit.OpeningBalance);

                if (debt > 0 && unit.OpeningBalanceDate.HasValue)
                    rows.Add(BuildOpeningBalanceRow(unit, debt));
            }

            if (credit > 0)
            {
                credit = ApplyCreditToRows(unitRows, credit, collectionCredit?.LastDate ?? unit.OpeningBalanceDate);
            }

            if (unit.OpeningBalance > 0)
            {
                // Kullanılmayan kredi varsa ek bir bilgilendirme satırı (alacaklı)
                if (credit > 0 && unit.OpeningBalanceDate.HasValue)
                {
                    rows.Add(BuildOpeningBalanceRow(unit, -credit));
                }
            }
        }
    }

    private static decimal ApplyCreditToRows(List<DuesListItemViewModel> unitRows, decimal credit, DateTime? creditDate)
    {
        foreach (var row in unitRows.Where(r => !r.IsPaid).OrderBy(r => r.AccrualDate).ThenBy(r => r.PaymentOrDueDate))
        {
            if (credit <= 0) break;
            var reduction = Math.Min(row.RemainingAmount, credit);
            row.RemainingAmount -= reduction;
            credit -= reduction;
            if (row.RemainingAmount <= 0)
            {
                row.IsPaid = true;
                row.RemainingAmount = 0;
                // Ödendi rozetinde gerçek son tahsilat tarihini göster (varsa);
                // hiç tahsilat yoksa kredi/devir tarihi, o da yoksa mevcut tarih.
                row.PaymentOrDueDate = row.LastPaymentDate
                    ?? creditDate
                    ?? row.PaymentOrDueDate;
            }
        }

        return credit;
    }

    private static DuesListItemViewModel BuildOpeningBalanceRow(Unit unit, decimal remainingAmount)
    {
        var date = unit.OpeningBalanceDate ?? DateTime.Today;
        return new DuesListItemViewModel
        {
            Id = 0,
            UnitId = unit.Id,
            Period = "Devir",
            BlockName = unit.Block?.Name ?? "-",
            UnitNo = unit.UnitNo,
            OwnerName = unit.OwnerName ?? string.Empty,
            UnitDisplay = UnitDisplayHelper.Display(unit),
            DuesTypeName = "Devir Bakiyesi",
            AccrualDate = date,
            PaymentOrDueDate = date,
            IsPaid = remainingAmount <= 0,
            IsOverdue = false,
            Amount = remainingAmount,
            RemainingAmount = remainingAmount,
            IsOpeningBalance = true
        };
    }

    private static Unit? FirstActiveGroupUnit(BillingGroup? group)
    {
        return group?.Units
            .Where(x => x.Unit is { Active: true })
            .Select(x => x.Unit)
            .OrderBy(x => x!.Block!.Name)
            .ThenBy(x => x!.UnitNo)
            .FirstOrDefault();
    }
}
