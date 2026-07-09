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
    IReportingService reportingService,
    MobileScopeService scope,
    UnitLedgerService unitLedgerService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var allowedUnitIds = await scope.GetAllowedUnitIdsAsync(User);
        if (allowedUnitIds is not null)
        {
            // Sakin: kendi dairelerine göre sade panel.
            return View("Resident", await BuildResidentPanelAsync(allowedUnitIds));
        }

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

    private async Task<MobileResidentPanelViewModel> BuildResidentPanelAsync(IReadOnlyList<int> allowedUnitIds)
    {
        if (allowedUnitIds.Count == 0)
        {
            return new MobileResidentPanelViewModel { HasUnits = false };
        }

        var units = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .Where(x => allowedUnitIds.Contains(x.Id))
            .OrderBy(x => x.Block!.Name).ThenBy(x => x.UnitNo)
            .ToListAsync();

        var summaries = await unitLedgerService.BuildSummariesAsync(allowedUnitIds);
        var unitItems = units.Select(x => new MobileUnitListItem
        {
            Id = x.Id,
            UnitNo = x.UnitNo,
            Display = x.Block is null ? x.UnitNo : $"{x.Block.Name}-{x.UnitNo}",
            OwnerName = x.OwnerName,
            Balance = summaries.TryGetValue(x.Id, out var s) ? s.NetBalance : 0m
        }).ToList();

        var allowed = allowedUnitIds.ToList();
        var payments = await db.Collections.AsNoTracking()
            .Include(x => x.Unit).ThenInclude(x => x!.Block)
            .Where(x => allowed.Contains(x.UnitId))
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Take(5)
            .ToListAsync();

        var announcements = await db.Announcements.AsNoTracking()
            .Where(x => x.IsPublished)
            .OrderByDescending(x => x.PublishDate)
            .Take(3)
            .ToListAsync();

        return new MobileResidentPanelViewModel
        {
            HasUnits = true,
            UnitCount = unitItems.Count,
            Balance = unitItems.Sum(x => x.Balance),
            Units = unitItems,
            RecentPayments = payments.Select(x => new MobileResidentPaymentRow
            {
                Date = x.Date,
                UnitDisplay = x.Unit?.Block is null ? (x.Unit?.UnitNo ?? "") : $"{x.Unit.Block.Name}-{x.Unit.UnitNo}",
                Amount = x.Amount
            }).ToList(),
            RecentAnnouncements = announcements
        };
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
