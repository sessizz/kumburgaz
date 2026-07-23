using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[ModuleAuthorize(AppModules.Aidatlar)]
public class DuesController(
    ApplicationDbContext db,
    ICollectionService collectionService,
    AccountAssignmentService accountAssignmentService,
    IDuesLedgerRowService ledgerService) : Controller
{
    public const string AllPeriodsValue = "all";

    public async Task<IActionResult> Index(string? q = null, string? tab = null, string? period = null)
    {
        var periods = await ledgerService.GetAvailablePeriodsAsync();
        var selectedPeriod = ResolvePeriod(period, periods);

        var rows = await ledgerService.GetInstallmentRowsAsync();

        if (selectedPeriod != AllPeriodsValue)
        {
            rows = rows.Where(x => x.IsOpeningBalance || x.Period == selectedPeriod).ToList();
        }

        var query = q?.Trim();
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
            ActiveTab = string.Equals(tab, "collections", StringComparison.OrdinalIgnoreCase) ? "collections" : "dues",
            SelectedPeriod = selectedPeriod,
            PeriodOptions = BuildPeriodOptions(periods, selectedPeriod)
        });
    }

    internal static string ResolvePeriod(string? period, List<string> availablePeriods)
    {
        if (period is null)
        {
            return PeriodHelper.CurrentFiscalPeriod(DateTime.Today);
        }

        if (string.Equals(period, AllPeriodsValue, StringComparison.OrdinalIgnoreCase))
        {
            return AllPeriodsValue;
        }

        return PeriodHelper.IsValid(period) && availablePeriods.Contains(period)
            ? period
            : PeriodHelper.CurrentFiscalPeriod(DateTime.Today);
    }

    internal static List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> BuildPeriodOptions(List<string> periods, string selectedPeriod)
    {
        var options = new List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>
        {
            new("Tüm Dönemler", AllPeriodsValue, selectedPeriod == AllPeriodsValue)
        };
        options.AddRange(periods.Select(p => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(p, p, p == selectedPeriod)));
        return options;
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
}
