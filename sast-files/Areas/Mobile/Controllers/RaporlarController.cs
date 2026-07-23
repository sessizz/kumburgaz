using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Globalization;

namespace Kumburgaz.Web.Areas.Mobile.Controllers;

// Sakin'in bu modul icin gorunum yetkisi yok (SeedRolePermissionsAsync), bu yuzden
// mobil ekranda MobileScopeService'e gerek yok: masaustuyle ayni sekilde yalnizca personel/yonetici gorur.
[Area("Mobile")]
[ModuleAuthorize(AppModules.Raporlar)]
public class RaporlarController(
    ApplicationDbContext db,
    IReportingService reportingService) : Controller
{
    public async Task<IActionResult> Index(string? ay = null)
    {
        var todayUtc = DateTime.UtcNow.Date;
        var options = BuildMonthOptions(todayUtc, ay, out var selectedMonth, out var selectedStart, out var selectedEnd);

        var debtReport = await reportingService.GetDuesDebtReportAsync(new DuesDebtReportQuery());
        var summary = DuesDebtSummaryHelper.Build(debtReport);

        var categoryExpenses = await CategoryExpenseHelper.GetAsync(db, selectedStart, selectedEnd);

        return View(new MobileRaporlarViewModel
        {
            DebtorCount = summary.DebtorCount,
            TotalDebt = summary.TotalDebt,
            CreditorCount = summary.CreditorCount,
            TotalCredit = summary.TotalCredit,
            SelectedMonth = selectedMonth,
            SelectedMonthLabel = selectedStart.ToString("MMMM yyyy", new CultureInfo("tr-TR")),
            MonthOptions = options,
            CategoryExpenses = categoryExpenses,
            CategoryExpenseTotal = categoryExpenses.Sum(x => x.Amount)
        });
    }

    // Son 12 ayi "yyyy-MM" degeriyle dropdown'a doldurur; secili olani da cozer.
    private static List<SelectListItem> BuildMonthOptions(
        DateTime todayUtc,
        string? requestedMonth,
        out string selectedMonth,
        out DateTime selectedStart,
        out DateTime selectedEnd)
    {
        var currentMonthStart = new DateTime(todayUtc.Year, todayUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var culture = new CultureInfo("tr-TR");

        var options = new List<SelectListItem>();
        var monthStarts = new List<DateTime>();
        for (var i = 0; i < 12; i++)
        {
            var start = currentMonthStart.AddMonths(-i);
            monthStarts.Add(start);
            options.Add(new SelectListItem(start.ToString("MMMM yyyy", culture), start.ToString("yyyy-MM")));
        }

        selectedStart = currentMonthStart;
        if (!string.IsNullOrWhiteSpace(requestedMonth)
            && DateTime.TryParseExact(requestedMonth, "yyyy-MM", culture, DateTimeStyles.None, out var parsed))
        {
            var match = monthStarts.FirstOrDefault(x => x.Year == parsed.Year && x.Month == parsed.Month);
            if (match != default)
            {
                selectedStart = DateTime.SpecifyKind(match, DateTimeKind.Utc);
            }
        }

        selectedEnd = selectedStart.AddMonths(1);
        selectedMonth = selectedStart.ToString("yyyy-MM");

        foreach (var opt in options)
        {
            opt.Selected = opt.Value == selectedMonth;
        }

        return options;
    }
}
