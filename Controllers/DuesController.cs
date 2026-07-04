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
    ICollectionService collectionService,
    AccountAssignmentService accountAssignmentService) : Controller
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
            .Include(x => x.ResponsibleAccount)
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

        rows = rows
            .Where(x => string.IsNullOrWhiteSpace(query) ||
                        x.Period.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.BlockName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.UnitNo.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.OwnerName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.ResponsibleAccountName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
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

    public async Task<IActionResult> CreateInstallment()
    {
        var period = PeriodHelper.CurrentFiscalPeriod(DateTime.Today);
        var startYear = int.Parse(period[..4]);
        var model = new DuesInstallmentCreateViewModel
        {
            Period = period,
            AccrualDate = new DateTime(startYear, 7, 1),
            DueDate = new DateTime(startYear, 7, 31),
            PayerType = DuesPayerType.Owner
        };

        return View(await BuildInstallmentFormAsync(model));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateInstallment(DuesInstallmentCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(await BuildInstallmentFormAsync(model));
        }

        var group = await db.BillingGroups
            .AsNoTracking()
            .Include(x => x.DuesType)
            .FirstOrDefaultAsync(x => x.Id == model.BillingGroupId && x.Active);
        if (group is null)
        {
            ModelState.AddModelError(nameof(model.BillingGroupId), "Aktif aidat grubu bulunamadı.");
            return View(await BuildInstallmentFormAsync(model));
        }

        var unitExists = await db.Units.AsNoTracking().AnyAsync(x => x.Id == model.UnitId && x.Active);
        if (!unitExists)
        {
            ModelState.AddModelError(nameof(model.UnitId), "Aktif daire bulunamadı.");
            return View(await BuildInstallmentFormAsync(model));
        }

        if (model.Amount <= 0)
        {
            model.Amount = group.DuesType?.Amount ?? 0m;
        }

        var exists = await db.DuesInstallments.AnyAsync(x =>
            x.BillingGroupId == model.BillingGroupId &&
            x.UnitId == model.UnitId &&
            x.Period == model.Period);
        if (exists)
        {
            ModelState.AddModelError(string.Empty, "Bu daire ve dönem için aidat borcu zaten var.");
            return View(await BuildInstallmentFormAsync(model));
        }

        var responsibleAccountId = await accountAssignmentService.ResolveResponsibleAccountIdAsync(model.UnitId, model.PayerType);
        db.DuesInstallments.Add(new DuesInstallment
        {
            BillingGroupId = model.BillingGroupId,
            UnitId = model.UnitId,
            ResponsibleAccountId = responsibleAccountId,
            Period = model.Period,
            AccrualDate = DateTimeHelper.EnsureUtc(model.AccrualDate),
            DueDate = DateTimeHelper.EnsureUtc(model.DueDate),
            Amount = model.Amount,
            RemainingAmount = model.Amount,
            Status = InstallmentStatus.Open
        });

        await db.SaveChangesAsync();
        await CollectionAdvanceAllocator.ApplyAsync(db);
        TempData["Success"] = "Aidat borcu eklendi.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<DuesInstallmentCreateViewModel> BuildInstallmentFormAsync(DuesInstallmentCreateViewModel model)
    {
        var groups = await db.BillingGroups.AsNoTracking()
            .Where(x => x.Active)
            .Include(x => x.DuesType)
            .OrderBy(x => x.Name)
            .ToListAsync();

        model.BillingGroupOptions = groups
            .Select(x => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(
                $"{x.Name} ({x.DuesType?.Name ?? "Aidat"} - {(x.DuesType?.Amount ?? 0m):N2} TL)",
                x.Id.ToString(),
                x.Id == model.BillingGroupId))
            .ToList();

        var units = await db.Units.AsNoTracking()
            .Where(x => x.Active)
            .Include(x => x.Block)
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .ToListAsync();

        model.UnitOptions = units
            .Select(x => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(
                $"{x.Block?.Name ?? "-"}-{x.UnitNo}",
                x.Id.ToString(),
                x.Id == model.UnitId))
            .ToList();

        model.PayerTypeOptions = AccountDisplayHelper.PayerTypeOptions(model.PayerType);
        return model;
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

        var collectionCredits = await CollectionCreditHelper.BuildUnitCreditMapAsync(db);

        foreach (var unit in units)
        {
            var unitRows = rows.Where(r => r.UnitId == unit.Id).ToList();
            var collectionCredit = collectionCredits.GetValueOrDefault(unit.Id);

            if (unit.OpeningBalance > 0)
            {
                // Alacak: ödenmemiş taksitlerin kalanından düş (en eski tarihten başla)
                var credit = unit.OpeningBalance + collectionCredit;
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
                        // hiç tahsilat yoksa devir bakiyesi tarihi, o da yoksa DueDate.
                        row.PaymentOrDueDate = row.LastPaymentDate
                            ?? unit.OpeningBalanceDate
                            ?? row.PaymentOrDueDate;
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
                // Borç: tahsis edilmemiş tahsilat fazlası varsa önce devreden borcu kapatır.
                var debt = -unit.OpeningBalance;
                var appliedCredit = Math.Min(debt, collectionCredit);
                debt -= appliedCredit;

                if (debt > 0 && unit.OpeningBalanceDate.HasValue)
                    rows.Add(BuildOpeningBalanceRow(unit, debt));
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
