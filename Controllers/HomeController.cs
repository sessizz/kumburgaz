using System.Diagnostics;
using Kumburgaz.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

public class HomeController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var todayUtc = DateTime.UtcNow.Date;
        var monthStart = new DateTime(todayUtc.Year, todayUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonthStart = monthStart.AddMonths(1);

        var openingCredit = await db.Units.Where(x => x.Active && x.OpeningBalance > 0).SumAsync(x => (decimal?)x.OpeningBalance) ?? 0m;
        var openingDebt   = await db.Units.Where(x => x.Active && x.OpeningBalance < 0).SumAsync(x => (decimal?)(-x.OpeningBalance)) ?? 0m;

        var actualCollections = await db.Collections.SumAsync(x => (decimal?)x.Amount) ?? 0m;
        var totalGenerated = await db.DuesInstallments.SumAsync(x => (decimal?)x.Amount) ?? 0m;
        var totalCharge = totalGenerated + openingDebt;
        var totalCollections = actualCollections + openingCredit;
        var collectionRate = totalCharge > 0 ? Math.Min(100m, totalCollections / totalCharge * 100m) : 0m;

        var ledgerIncome = await db.LedgerTransactions
            .Where(x => !x.IsTransfer && x.IncomeExpenseCategory != null && x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gelir)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;
        var ledgerExpense = await db.LedgerTransactions
            .Where(x => !x.IsTransfer && x.IncomeExpenseCategory != null && x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gider)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;

        var collections = await db.Collections
            .Select(x => new { x.CashBoxId, x.BankAccountId, x.Amount })
            .ToListAsync();
        var ledgerRows = await db.LedgerTransactions
            .Include(x => x.IncomeExpenseCategory)
            .Select(x => new
            {
                x.CashBoxId,
                x.BankAccountId,
                x.Amount,
                x.IsTransfer,
                x.TransferIsIncoming,
                Type = x.IncomeExpenseCategory != null ? x.IncomeExpenseCategory.Type : CategoryTypeHelper.Gider
            })
            .ToListAsync();
        var cashBoxes = await db.CashBoxes.Where(x => x.Active).OrderBy(x => x.Name).ToListAsync();
        var bankAccounts = await db.BankAccounts.Where(x => x.Active).OrderBy(x => x.Name).ThenBy(x => x.Branch).ToListAsync();

        var cashBankItems = new List<CashBankListItemViewModel>();
        cashBankItems.AddRange(cashBoxes.Select(x => new CashBankListItemViewModel
        {
            Type = "cash",
            Name = x.Name,
            Balance = x.OpeningBalance
                + collections.Where(c => c.CashBoxId == x.Id).Sum(c => c.Amount)
                + ledgerRows.Where(e => e.CashBoxId == x.Id).Sum(e => e.IsTransfer
                    ? (e.TransferIsIncoming ? e.Amount : -e.Amount)
                    : (e.Type == CategoryTypeHelper.Gelir ? e.Amount : -e.Amount))
        }));
        cashBankItems.AddRange(bankAccounts.Select(x => new CashBankListItemViewModel
        {
            Type = "bank",
            Name = string.IsNullOrWhiteSpace(x.Branch) ? x.Name : $"{x.Name} - {x.Branch}",
            Balance = x.OpeningBalance
                + collections.Where(c => c.BankAccountId == x.Id).Sum(c => c.Amount)
                + ledgerRows.Where(e => e.BankAccountId == x.Id).Sum(e => e.IsTransfer
                    ? (e.TransferIsIncoming ? e.Amount : -e.Amount)
                    : (e.Type == CategoryTypeHelper.Gelir ? e.Amount : -e.Amount))
        }));

        var monthCollections = await db.Collections
            .Where(x => x.Date >= monthStart && x.Date < nextMonthStart)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;
        var monthCollectionCount = await db.Collections.CountAsync(x => x.Date >= monthStart && x.Date < nextMonthStart);

        var expenseForecast = await BuildExpenseForecastAsync(monthStart);
        var forecastExpense = expenseForecast.Sum(x => x.Amount);

        var overdueSnapshot = await BuildOverdueSnapshotAsync(todayUtc);

        var upcomingExpenses = expenseForecast.Take(5).Select((x, index) => new DashboardUpcomingExpense
        {
            Name = x.Name,
            Amount = x.Amount,
            Date = monthStart.AddDays(Math.Min(27, 5 + index * 5)),
            Status = index == 4 ? "Planlandı" : "Bekliyor"
        }).ToList();

        var vm = new DashboardViewModel
        {
            CollectionRate = collectionRate,
            TotalGenerated = totalGenerated,
            OverdueDebt = overdueSnapshot.TotalDebt,
            OverdueUnitCount = overdueSnapshot.UnitCount,
            ForecastExpense = forecastExpense,
            MonthCollections = monthCollections,
            MonthCollectionCount = monthCollectionCount,
            CashBankBalance = cashBankItems.Sum(x => x.Balance),
            NetPosition = monthCollections - forecastExpense,
            ActiveUnits = await db.Units.CountAsync(x => x.Active),
            OpenRequestCount = await db.ServiceRequests.CountAsync(x => x.Status == ServiceRequestStatus.Open || x.Status == ServiceRequestStatus.InProgress),
            ExpenseForecast = expenseForecast,
            Cashflow = await BuildCashflowAsync(monthStart),
            OverdueItems = overdueSnapshot.Items,
            UpcomingExpenses = upcomingExpenses,
            RecentRequests = await db.ServiceRequests.AsNoTracking()
                .Include(x => x.Unit).ThenInclude(x => x!.Block)
                .OrderByDescending(x => x.Priority)
                .ThenByDescending(x => x.CreatedAt)
                .Take(5)
                .ToListAsync(),
            RecentAnnouncements = await db.Announcements.AsNoTracking()
                .Where(x => x.IsPublished)
                .OrderByDescending(x => x.PublishDate)
                .Take(3)
                .ToListAsync(),
            CalendarDays = BuildCalendarDays(todayUtc)
        };

        return View(vm);
    }

    private async Task<DashboardOverdueSnapshot> BuildOverdueSnapshotAsync(DateTime todayUtc)
    {
        var installments = await db.DuesInstallments.AsNoTracking()
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.DuesType)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.CombinedUnitMembers)
            .ThenInclude(x => x.ComponentUnit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.ResponsibleAccount)
            .Where(x => x.RemainingAmount > 0)
            .Where(x => x.UnitId == null || x.Unit!.Active)
            .OrderBy(x => x.UnitId)
            .ThenBy(x => x.AccrualDate)
            .ThenBy(x => x.DueDate)
            .ThenBy(x => x.Id)
            .ToListAsync();

        var collectionCredits = await CollectionCreditHelper.BuildUnitCreditMapAsync(db);
        var items = new List<DashboardOverdueItem>();

        foreach (var group in installments.Where(x => x.UnitId.HasValue).GroupBy(x => x.UnitId!.Value))
        {
            var first = group.First();
            var unit = first.Unit;
            if (unit is null)
            {
                continue;
            }

            var credit = collectionCredits.GetValueOrDefault(unit.Id);
            if (unit.OpeningBalance > 0)
            {
                credit += unit.OpeningBalance;
            }

            var overdueAmount = 0m;
            var oldestOverdueDate = DateTime.MaxValue;

            if (unit.OpeningBalance < 0)
            {
                var openingDebt = -unit.OpeningBalance;
                var appliedCredit = Math.Min(openingDebt, credit);
                openingDebt -= appliedCredit;
                credit -= appliedCredit;

                var openingDate = unit.OpeningBalanceDate?.Date ?? DateTime.MinValue;
                if (openingDebt > 0 && openingDate < todayUtc)
                {
                    overdueAmount += openingDebt;
                    oldestOverdueDate = openingDate;
                }
            }

            foreach (var installment in group)
            {
                var remaining = installment.RemainingAmount;
                if (credit > 0)
                {
                    var reduction = Math.Min(remaining, credit);
                    remaining -= reduction;
                    credit -= reduction;
                }

                if (remaining <= 0 || installment.DueDate.Date >= todayUtc)
                {
                    continue;
                }

                overdueAmount += remaining;
                if (installment.DueDate.Date < oldestOverdueDate)
                {
                    oldestOverdueDate = installment.DueDate.Date;
                }
            }

            if (overdueAmount <= 0)
            {
                continue;
            }

            items.Add(new DashboardOverdueItem
            {
                UnitDisplay = UnitDisplayHelper.Display(unit),
                OwnerName = first.ResponsibleAccount?.Name ?? unit.OwnerName ?? string.Empty,
                Amount = overdueAmount,
                Days = Math.Max(1, (int)(todayUtc - oldestOverdueDate).TotalDays)
            });
        }

        var overdueUnitCount = items.Count;

        foreach (var installment in installments.Where(x => !x.UnitId.HasValue && x.DueDate.Date < todayUtc))
        {
            items.Add(new DashboardOverdueItem
            {
                UnitDisplay = installment.BillingGroup?.Name ?? "Aidat",
                OwnerName = installment.ResponsibleAccount?.Name ?? string.Empty,
                Amount = installment.RemainingAmount,
                Days = Math.Max(1, (int)(todayUtc - installment.DueDate.Date).TotalDays)
            });
        }

        return new DashboardOverdueSnapshot(
            items.Sum(x => x.Amount),
            overdueUnitCount,
            items
                .OrderByDescending(x => x.Amount)
                .ThenByDescending(x => x.Days)
                .ThenBy(x => x.UnitDisplay)
                .Take(5)
                .ToList());
    }

    private async Task<List<ExpenseForecastItem>> BuildExpenseForecastAsync(DateTime monthStart)
    {
        var previousStart = monthStart.AddMonths(-6);
        var rows = await db.LedgerTransactions.AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Where(x => x.Date >= previousStart &&
                        x.Date < monthStart &&
                        !x.IsTransfer &&
                        x.IncomeExpenseCategory != null &&
                        x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gider)
            .GroupBy(x => x.IncomeExpenseCategory!.Name)
            .Select(x => new { Name = x.Key, Amount = x.Sum(t => t.Amount) / 6m })
            .OrderByDescending(x => x.Amount)
            .ToListAsync();

        if (rows.Count == 0)
        {
            rows =
            [
                new { Name = "Maaşlar", Amount = 186000m },
                new { Name = "Elektrik", Amount = 72000m },
                new { Name = "Temizlik", Amount = 48000m },
                new { Name = "Güvenlik", Amount = 46000m },
                new { Name = "Bakım", Amount = 36900m },
                new { Name = "Ortak Alan", Amount = 24000m }
            ];
        }

        var total = rows.Sum(x => x.Amount);
        var colors = new[] { "#3b82f6", "#06b6d4", "#10b981", "#f59e0b", "#fb923c", "#ef4444" };
        return rows.Take(6).Select((x, index) => new ExpenseForecastItem
        {
            Name = x.Name,
            Amount = x.Amount,
            Percent = total > 0 ? x.Amount / total * 100m : 0m,
            Color = colors[index % colors.Length]
        }).ToList();
    }

    private async Task<List<DashboardCashflowMonth>> BuildCashflowAsync(DateTime monthStart)
    {
        var start = monthStart.AddMonths(-5);
        var collections = await db.Collections.AsNoTracking()
            .Where(x => x.Date >= start && x.Date < monthStart.AddMonths(1))
            .GroupBy(x => new { x.Date.Year, x.Date.Month })
            .Select(x => new { x.Key.Year, x.Key.Month, Amount = x.Sum(t => t.Amount) })
            .ToListAsync();

        var expenses = await db.LedgerTransactions.AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Where(x => x.Date >= start &&
                        x.Date < monthStart.AddMonths(1) &&
                        !x.IsTransfer &&
                        x.IncomeExpenseCategory != null &&
                        x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gider)
            .GroupBy(x => new { x.Date.Year, x.Date.Month })
            .Select(x => new { x.Key.Year, x.Key.Month, Amount = x.Sum(t => t.Amount) })
            .ToListAsync();

        var months = new List<DashboardCashflowMonth>();
        for (var i = 0; i < 6; i++)
        {
            var date = start.AddMonths(i);
            months.Add(new DashboardCashflowMonth
            {
                Month = date.ToString("MMM"),
                Income = collections.FirstOrDefault(x => x.Year == date.Year && x.Month == date.Month)?.Amount ?? 0m,
                Expense = expenses.FirstOrDefault(x => x.Year == date.Year && x.Month == date.Month)?.Amount ?? 0m
            });
        }

        if (months.All(x => x.Income == 0 && x.Expense == 0))
        {
            var sampleIncome = new[] { 490000m, 650000m, 710000m, 580000m, 560000m, 670000m };
            var sampleExpense = new[] { 280000m, 380000m, 320000m, 410000m, 350000m, 370000m };
            for (var i = 0; i < months.Count; i++)
            {
                months[i].Income = sampleIncome[i];
                months[i].Expense = sampleExpense[i];
            }
        }

        return months;
    }

    private static List<DashboardCalendarDay> BuildCalendarDays(DateTime todayUtc)
    {
        var start = todayUtc.AddDays(-7);
        var names = new[] { "Paz", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt" };
        var markers = new[] { "r", "b", "g", "b", "g", "r", "g", "r", "b", "b", "g", "r", "b", "g" };
        return Enumerable.Range(0, 14).Select(i =>
        {
            var date = start.AddDays(i);
            return new DashboardCalendarDay
            {
                Weekday = names[(int)date.DayOfWeek],
                Day = date.Day,
                Marker = markers[i % markers.Length],
                IsToday = date.Date == todayUtc.Date
            };
        }).ToList();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private sealed record DashboardOverdueSnapshot(decimal TotalDebt, int UnitCount, List<DashboardOverdueItem> Items);
}
