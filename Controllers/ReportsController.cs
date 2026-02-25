using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

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
}
