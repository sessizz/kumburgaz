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
        var ledgerQuery = BuildLedgerQueryFromRequest();
        await PopulateFiltersAsync(query, ledgerQuery);
        var rows = await reportingService.GetDuesDebtReportAsync(query);
        var ledgerRows = await GetLedgerReportRowsAsync(ledgerQuery);
        return View(new ReportsOverviewViewModel
        {
            DuesQuery = query,
            DuesRows = rows,
            LedgerQuery = ledgerQuery,
            LedgerRows = ledgerRows
        });
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
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.Units)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.CombinedUnitMembers)
            .ThenInclude(x => x.ComponentUnit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.CombinedUnitMembers)
            .ThenInclude(x => x.ComponentUnit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.ResponsibleAccount)
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
            AccrualDate = installment.AccrualDate,
            DueDate = installment.DueDate,
            Amount = installment.Amount,
            PaidAmount = paidAmount,
            RemainingAmount = installment.RemainingAmount,
            UnitDisplay = installment.UnitId.HasValue
                ? UnitDisplayHelper.Display(installment.Unit)
                : BillingGroupDisplayHelper.UnitDisplay(installment.BillingGroup),
            BillingGroupName = installment.BillingGroup?.Name ?? "-",
            ResponsibleAccountName = installment.ResponsibleAccount?.Name ?? "-",
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
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.Units)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.CombinedUnitMembers)
            .ThenInclude(x => x.ComponentUnit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.CombinedUnitMembers)
            .ThenInclude(x => x.ComponentUnit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.ResponsibleAccount)
            .Include(x => x.Allocations)
            .FirstOrDefaultAsync(x => x.Id == model.Id);

        if (installment is null)
        {
            return NotFound();
        }

        var paidAmount = installment.Allocations.Sum(x => x.AppliedAmount);
        installment.Period = model.Period;
        installment.AccrualDate = DateTimeHelper.EnsureUtc(model.AccrualDate);
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
                ? UnitDisplayHelper.Display(installment.Unit)
                : BillingGroupDisplayHelper.UnitDisplay(installment.BillingGroup);
            model.BillingGroupName = installment.BillingGroup?.Name ?? "-";
            model.ResponsibleAccountName = installment.ResponsibleAccount?.Name ?? "-";
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

    private async Task PopulateFiltersAsync(DuesDebtReportQuery duesQuery, LedgerReportQuery ledgerQuery)
    {
        ViewBag.Blocks = await db.Blocks
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString(), duesQuery.BlockId == x.Id))
            .ToListAsync();

        ViewBag.DuesTypes = await db.DuesTypes
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString(), duesQuery.DuesTypeId == x.Id))
            .ToListAsync();

        ViewBag.BillingGroups = await db.BillingGroups
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString(), duesQuery.BillingGroupId == x.Id))
            .ToListAsync();

        var selectedLedgerCategories = ledgerQuery.LedgerCategoryIds.ToHashSet();
        ViewBag.LedgerCategories = await db.IncomeExpenseCategories
            .AsNoTracking()
            .Where(x => x.Active)
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .Select(x => new SelectListItem(
                $"{CategoryTypeHelper.Display(x.Type)} - {x.Name}",
                x.Id.ToString(),
                selectedLedgerCategories.Contains(x.Id)))
            .ToListAsync();
    }

    private LedgerReportQuery BuildLedgerQueryFromRequest()
    {
        var selectedCategories = Request.Query["LedgerCategoryIds"]
            .SelectMany(x => x?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
            .Select(x => int.TryParse(x, out var id) ? id : (int?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        return new LedgerReportQuery
        {
            LedgerType = Request.Query["LedgerType"].FirstOrDefault(),
            LedgerCategoryIds = selectedCategories,
            LedgerStartDate = TryReadDate("LedgerStartDate"),
            LedgerEndDate = TryReadDate("LedgerEndDate")
        };
    }

    private DateTime? TryReadDate(string key)
    {
        return DateTime.TryParse(Request.Query[key].FirstOrDefault(), out var value)
            ? value.Date
            : null;
    }

    private async Task<List<LedgerReportRow>> GetLedgerReportRowsAsync(LedgerReportQuery query)
    {
        var ledgerType = CategoryTypeHelper.Normalize(query.LedgerType);
        var filterByType = !string.IsNullOrWhiteSpace(query.LedgerType)
            && ledgerType is CategoryTypeHelper.Gelir or CategoryTypeHelper.Gider;
        var categoryIds = query.LedgerCategoryIds.ToHashSet();

        var rowsQuery = db.LedgerTransactions
            .AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Include(x => x.CashBox)
            .Include(x => x.BankAccount)
            .Where(x => !x.IsTransfer && x.IncomeExpenseCategory != null);

        if (filterByType)
        {
            rowsQuery = rowsQuery.Where(x => x.IncomeExpenseCategory!.Type == ledgerType);
        }

        if (categoryIds.Count > 0)
        {
            rowsQuery = rowsQuery.Where(x => x.IncomeExpenseCategoryId.HasValue && categoryIds.Contains(x.IncomeExpenseCategoryId.Value));
        }

        if (query.LedgerStartDate.HasValue)
        {
            var start = DateTimeHelper.EnsureUtc(query.LedgerStartDate.Value.Date);
            rowsQuery = rowsQuery.Where(x => x.Date >= start);
        }

        if (query.LedgerEndDate.HasValue)
        {
            var endExclusive = DateTimeHelper.EnsureUtc(query.LedgerEndDate.Value.Date.AddDays(1));
            rowsQuery = rowsQuery.Where(x => x.Date < endExclusive);
        }

        return await rowsQuery
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .Select(x => new LedgerReportRow
            {
                Id = x.Id,
                Date = x.Date,
                CategoryType = x.IncomeExpenseCategory!.Type,
                CategoryName = x.IncomeExpenseCategory.Name,
                Amount = x.Amount,
                PaymentChannelName = EnumDisplayHelper.Display(x.PaymentChannel),
                AccountName = x.CashBox != null
                    ? x.CashBox.Name
                    : x.BankAccount != null
                        ? x.BankAccount.Name
                        : string.Empty,
                Description = x.Description ?? string.Empty
            })
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
