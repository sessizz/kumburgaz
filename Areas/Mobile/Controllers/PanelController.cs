using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Areas.Mobile.Controllers;

[Area("Mobile")]
[ModuleAuthorize(AppModules.Panel)]
public class PanelController(
    ApplicationDbContext db,
    IReportingService reportingService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var todayUtc = DateTime.UtcNow.Date;
        var monthStart = new DateTime(todayUtc.Year, todayUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonthStart = monthStart.AddMonths(1);
        var lastMonthStart = monthStart.AddMonths(-1);

        var totalCollections = await db.Collections.SumAsync(x => (decimal?)x.Amount) ?? 0m;
        var monthCollections = await db.Collections
            .Where(x => x.Date >= monthStart && x.Date < nextMonthStart)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;

        var debtReport = await reportingService.GetDuesDebtReportAsync(new DuesDebtReportQuery());
        var summary = DuesDebtSummaryHelper.Build(debtReport);

        var monthExpenses = await ExpensesByCategoryAsync(monthStart, nextMonthStart);
        var lastMonthExpenses = await ExpensesByCategoryAsync(lastMonthStart, monthStart);

        var vm = new MobilePanelViewModel
        {
            TotalCollections = totalCollections,
            MonthCollections = monthCollections,
            DebtorCount = summary.DebtorCount,
            TotalDebt = summary.TotalDebt,
            TotalCredit = summary.TotalCredit,
            MonthLabel = monthStart.ToString("MMMM yyyy"),
            LastMonthLabel = lastMonthStart.ToString("MMMM yyyy"),
            MonthExpenses = monthExpenses.Take(3).ToList(),
            LastMonthExpenses = lastMonthExpenses.Take(3).ToList(),
            MonthExpenseTotal = monthExpenses.Sum(x => x.Amount),
            LastMonthExpenseTotal = lastMonthExpenses.Sum(x => x.Amount)
        };

        return View(vm);
    }

    private async Task<List<MobileCategoryAmount>> ExpensesByCategoryAsync(DateTime start, DateTime end)
    {
        return await db.LedgerTransactions
            .Where(x => !x.IsTransfer
                && x.Date >= start && x.Date < end
                && x.IncomeExpenseCategory != null
                && x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gider)
            .GroupBy(x => x.IncomeExpenseCategory!.Name)
            .Select(g => new MobileCategoryAmount { Name = g.Key, Amount = g.Sum(t => t.Amount) })
            .OrderByDescending(x => x.Amount)
            .ToListAsync();
    }
}
