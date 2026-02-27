using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Kumburgaz.Web.Controllers;

[Authorize]
public class ReportsController(
    ApplicationDbContext db,
    IReportingService reportingService) : Controller
{
    public async Task<IActionResult> DuesDebt([FromQuery] DuesDebtReportQuery query)
    {
        query.Period ??= PeriodHelper.CurrentFiscalPeriod(DateTime.Today);
        await PopulateFiltersAsync();
        var rows = await reportingService.GetDuesDebtReportAsync(query);
        ViewBag.Query = query;
        return View(rows);
    }

    public async Task<IActionResult> DuesDebtExcel([FromQuery] DuesDebtReportQuery query)
    {
        var rows = await reportingService.GetDuesDebtReportAsync(query);
        var bytes = reportingService.ExportDuesDebtAsExcel(rows);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "aidat-borc-raporu.xlsx");
    }

    public async Task<IActionResult> DuesDebtPdf([FromQuery] DuesDebtReportQuery query)
    {
        var rows = await reportingService.GetDuesDebtReportAsync(query);
        var bytes = reportingService.ExportDuesDebtAsPdf(rows);
        return File(bytes, "application/pdf", "aidat-borc-raporu.pdf");
    }

    public async Task<IActionResult> EditInstallment(int id, string? returnUrl = null)
    {
        var installment = await db.DuesInstallments
            .AsNoTracking()
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.Units)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Allocations)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (installment is null)
        {
            return NotFound();
        }

        var paidAmount = installment.Allocations.Sum(x => x.AppliedAmount);
        var model = new DuesInstallmentEditViewModel
        {
            Id = installment.Id,
            Period = installment.Period,
            DueDate = installment.DueDate,
            Amount = installment.Amount,
            PaidAmount = paidAmount,
            RemainingAmount = installment.RemainingAmount,
            UnitDisplay = installment.UnitId.HasValue
                ? $"{installment.Unit!.Block!.Name}-{installment.Unit.UnitNo}"
                : BillingGroupDisplayHelper.UnitDisplay(installment.BillingGroup),
            BillingGroupName = installment.BillingGroup?.Name ?? "-",
            ReturnUrl = returnUrl
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditInstallment(DuesInstallmentEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var installment = await db.DuesInstallments
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.Units)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Allocations)
            .FirstOrDefaultAsync(x => x.Id == model.Id);

        if (installment is null)
        {
            return NotFound();
        }

        var paidAmount = installment.Allocations.Sum(x => x.AppliedAmount);
        installment.Period = model.Period;
        installment.DueDate = DateTimeHelper.EnsureUtc(model.DueDate);
        installment.Amount = model.Amount;
        installment.RemainingAmount = model.Amount - paidAmount;
        installment.Status = ResolveInstallmentStatus(installment.Amount, installment.RemainingAmount);

        try
        {
            await db.SaveChangesAsync();
            TempData["Success"] = "Borç kaydı güncellendi.";
            return Redirect(model.ReturnUrl ?? Url.Action(nameof(DuesDebt))!);
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(string.Empty, "Aynı dönem için mükerrer borç kaydı oluşuyor.");

            model.PaidAmount = paidAmount;
            model.RemainingAmount = installment.RemainingAmount;
            model.UnitDisplay = installment.UnitId.HasValue
                ? $"{installment.Unit!.Block!.Name}-{installment.Unit.UnitNo}"
                : BillingGroupDisplayHelper.UnitDisplay(installment.BillingGroup);
            model.BillingGroupName = installment.BillingGroup?.Name ?? "-";
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteInstallment(int id, string? returnUrl = null)
    {
        var installment = await db.DuesInstallments
            .Include(x => x.Allocations)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (installment is null)
        {
            TempData["Error"] = "Borç kaydı bulunamadı.";
            return Redirect(returnUrl ?? Url.Action(nameof(DuesDebt))!);
        }

        if (installment.Allocations.Count > 0)
        {
            TempData["Error"] = "Tahsilat uygulanmış borç kaydı silinemez.";
            return Redirect(returnUrl ?? Url.Action(nameof(DuesDebt))!);
        }

        db.DuesInstallments.Remove(installment);
        await db.SaveChangesAsync();
        TempData["Success"] = "Borç kaydı silindi.";
        return Redirect(returnUrl ?? Url.Action(nameof(DuesDebt))!);
    }

    private async Task PopulateFiltersAsync()
    {
        ViewBag.Blocks = await db.Blocks
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString()))
            .ToListAsync();

        ViewBag.DuesTypes = await db.DuesTypes
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString()))
            .ToListAsync();

        ViewBag.BillingGroups = await db.BillingGroups
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString()))
            .ToListAsync();
    }

    private static InstallmentStatus ResolveInstallmentStatus(decimal amount, decimal remainingAmount)
    {
        if (remainingAmount <= 0)
        {
            return InstallmentStatus.Paid;
        }

        if (remainingAmount < amount)
        {
            return InstallmentStatus.PartiallyPaid;
        }

        return InstallmentStatus.Open;
    }
}
