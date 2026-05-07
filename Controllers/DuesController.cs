using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize]
public class DuesController(
    ApplicationDbContext db,
    ICollectionService collectionService) : Controller
{
    public async Task<IActionResult> Index(string? q = null, string? tab = null)
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
            .ToListAsync();

        var query = q?.Trim();
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
                    UnitDisplay = unit is not null ? UnitDisplayHelper.Display(unit) : BillingGroupDisplayHelper.UnitDisplay(x.BillingGroup),
                    DuesTypeName = x.BillingGroup?.DuesType?.Name ?? "Aidat",
                    AccrualDate = x.AccrualDate,
                    PaymentOrDueDate = isPaid && paidDate.HasValue ? paidDate.Value : x.DueDate,
                    IsPaid = isPaid,
                    IsOverdue = !isPaid && x.DueDate.Date < DateTime.Today,
                    Amount = x.Amount,
                    RemainingAmount = x.RemainingAmount
                };
            })
            .ToList();

        // Devir bakiyelerini uygula
        await ApplyOpeningBalancesAsync(rows);

        rows = rows
            .Where(x => string.IsNullOrWhiteSpace(query) ||
                        x.Period.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.BlockName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.UnitNo.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.OwnerName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.UnitDisplay.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.IsPaid)
            .ThenBy(x => x.PaymentOrDueDate)
            .ThenBy(x => x.BlockName)
            .ThenBy(x => x.UnitNo)
            .ToList();

        var collections = await collectionService.GetAllAsync();
        return View(new DuesIndexViewModel
        {
            DuesItems = rows,
            Collections = collections,
            Query = query ?? string.Empty,
            ActiveTab = string.Equals(tab, "collections", StringComparison.OrdinalIgnoreCase) ? "collections" : "dues"
        });
    }

    /// <summary>
    /// Her dairenin OpeningBalance'ını aidat satırlarına yansıtır:
    /// pozitif (alacak) → en eski taksitlerin RemainingAmount'unu azaltır,
    /// negatif (borç) → ek bir "Devir Bakiyesi" satırı eklenir.
    /// </summary>
    private async Task ApplyOpeningBalancesAsync(List<DuesListItemViewModel> rows)
    {
        var units = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .Where(x => x.OpeningBalance != 0m)
            .ToListAsync();
        if (units.Count == 0) return;

        foreach (var unit in units)
        {
            var unitRows = rows.Where(r => r.UnitId == unit.Id).ToList();

            if (unit.OpeningBalance > 0)
            {
                // Alacak: ödenmemiş taksitlerin kalanından düş (en eski tarihten başla)
                var credit = unit.OpeningBalance;
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
                    }
                }
                // Kullanılmayan kredi varsa ek bir bilgilendirme satırı (alacaklı)
                if (credit > 0 && unit.OpeningBalanceDate.HasValue)
                {
                    rows.Add(BuildOpeningBalanceRow(unit, -credit));
                }
            }
            else
            {
                // Borç: ek satır olarak ekle (sadece tarihi varsa)
                if (unit.OpeningBalanceDate.HasValue)
                    rows.Add(BuildOpeningBalanceRow(unit, -unit.OpeningBalance));
            }
        }
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
