using System.Diagnostics;
using Kumburgaz.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[ModuleAuthorize(AppModules.Panel)]
public class HomeController(
    ApplicationDbContext db,
    IExpenseForecastService expenseForecastService,
    BackupService backupService,
    IDuesLedgerRowService ledgerService) : Controller
{
    public async Task<IActionResult> Index(string? period = null)
    {
        var todayUtc = DateTime.UtcNow.Date;
        var monthStart = new DateTime(todayUtc.Year, todayUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonthStart = monthStart.AddMonths(1);

        var periods = await ledgerService.GetAvailablePeriodsAsync();
        var selectedPeriod = DuesController.ResolvePeriod(period, periods);
        var duesRows = await ledgerService.GetInstallmentRowsAsync();
        var openingRows = duesRows.Where(x => x.IsOpeningBalance).ToList();
        var allDuesRows = duesRows.Where(x => !x.IsOpeningBalance).ToList();
        var periodRows = duesRows
            .Where(x => !x.IsOpeningBalance && (selectedPeriod == DuesController.AllPeriodsValue || x.Period == selectedPeriod))
            .ToList();

        var totalGenerated = periodRows.Sum(x => x.Amount);
        var overdueCarriedDebt = openingRows.Where(x => x.RemainingAmount > 0).Sum(x => x.RemainingAmount);
        // Gecikmiş aidat borcu seçili dönemle sınırlı değildir: geçmiş bir dönemden kalan ödenmemiş
        // borç, dönem değiştirilse bile hâlâ gerçek bir borçtur ve gizlenmemelidir.
        var overdueDuesDebt = allDuesRows.Where(x => !x.IsPaid && x.IsOverdue).Sum(x => x.RemainingAmount);
        var overdueDebt = overdueCarriedDebt + overdueDuesDebt;
        var overdueUnitCount = openingRows.Where(x => x.RemainingAmount > 0)
            .Concat(allDuesRows.Where(x => !x.IsPaid && x.IsOverdue))
            .Select(x => x.UnitId)
            .Where(x => x.HasValue)
            .Distinct()
            .Count();
        var totalCredit = openingRows.Where(x => x.RemainingAmount < 0).Sum(x => -x.RemainingAmount);
        var collectedInPeriod = totalGenerated - periodRows.Sum(x => x.RemainingAmount);
        var collectionRate = totalGenerated > 0 ? Math.Min(100m, collectedInPeriod / totalGenerated * 100m) : 0m;
        var overdueItems = openingRows.Where(x => x.RemainingAmount > 0)
            .Concat(allDuesRows.Where(x => !x.IsPaid && x.IsOverdue))
            .OrderByDescending(x => x.RemainingAmount)
            .ThenBy(x => x.UnitDisplay)
            .Take(5)
            .Select(x => new DashboardOverdueItem
            {
                UnitDisplay = x.UnitDisplay,
                // Devir (açılış bakiyesi) satırlarında sorumlu hesap adı hiç atanmaz; bu durumda
                // dairenin malik adını göster.
                OwnerName = string.IsNullOrWhiteSpace(x.ResponsibleAccountName) ? x.OwnerName : x.ResponsibleAccountName,
                Amount = x.RemainingAmount
            })
            .ToList();

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
        var monthOtherIncome = await db.LedgerTransactions
            .Where(x => x.Date >= monthStart && x.Date < nextMonthStart &&
                !x.IsTransfer &&
                x.IncomeExpenseCategory != null &&
                x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gelir)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;
        var monthExpense = await db.LedgerTransactions
            .Where(x => x.Date >= monthStart && x.Date < nextMonthStart &&
                !x.IsTransfer &&
                x.IncomeExpenseCategory != null &&
                x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gider)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;

        var forecast = await expenseForecastService.BuildAsync(monthStart);
        var expenseForecast = forecast.Items;
        var forecastExpense = forecast.Total;

        var consistencyIssueCount = await db.ConsistencyCheckResults.CountAsync(x => !x.Resolved);
        var importProblemRowCount = await db.ImportBatchRows.CountAsync(x =>
            x.Status == ImportRowStatus.Error ||
            x.Status == ImportRowStatus.Duplicate ||
            x.Status == ImportRowStatus.Skipped);
        var failedImportBatchCount = await db.ImportBatches.CountAsync(x => x.Status == ImportBatchStatus.Failed);
        var backupFiles = backupService.ListBackups();
        var lastBackupAt = backupFiles.OrderByDescending(x => x.CreatedAt).FirstOrDefault()?.CreatedAt;
        var alerts = new List<DashboardAlertViewModel>();
        if (overdueDebt > 0m)
        {
            alerts.Add(new DashboardAlertViewModel
            {
                Title = "Vadesi geçmiş borç",
                Message = $"{overdueUnitCount} dairede toplam {overdueDebt:N2} TL borç var.",
                Severity = "error",
                Icon = "error",
                Url = Url.Action("DuesDebt", "Reports", new { BalanceStatus = "debt" }),
                LinkText = "Borçluları aç"
            });
        }
        if (totalCredit > 0m)
        {
            alerts.Add(new DashboardAlertViewModel
            {
                Title = "Yüksek alacak/avans",
                Message = $"Toplam {totalCredit:N2} TL avans/alacak bakiyesi var.",
                Severity = "info",
                Icon = "savings",
                Url = Url.Action("DuesDebt", "Reports", new { BalanceStatus = "credit" }),
                LinkText = "Alacaklıları aç"
            });
        }
        if (consistencyIssueCount > 0)
        {
            alerts.Add(new DashboardAlertViewModel
            {
                Title = "Tutarlılık kontrolü",
                Message = $"{consistencyIssueCount} açık tutarlılık uyarısı var.",
                Severity = "warning",
                Icon = "rule_settings",
                Url = Url.Action("Index", "Audit"),
                LinkText = "Denetimi aç"
            });
        }
        if (importProblemRowCount > 0 || failedImportBatchCount > 0)
        {
            alerts.Add(new DashboardAlertViewModel
            {
                Title = "Import kontrolü",
                Message = $"{importProblemRowCount} problemli import satırı, {failedImportBatchCount} başarısız batch var.",
                Severity = "warning",
                Icon = "upload_file",
                Url = Url.Action("Index", "Audit"),
                LinkText = "Importları aç"
            });
        }
        if (monthCollections + monthOtherIncome < monthExpense)
        {
            alerts.Add(new DashboardAlertViewModel
            {
                Title = "Bu ay nakit eksi",
                Message = $"Bu ay gelir {monthCollections + monthOtherIncome:N2} TL, gider {monthExpense:N2} TL.",
                Severity = "warning",
                Icon = "trending_down",
                Url = Url.Action("IncomeExpenseSummary", "Reports"),
                LinkText = "Özeti aç"
            });
        }
        if (lastBackupAt is null || lastBackupAt.Value < DateTime.UtcNow.AddDays(-2))
        {
            alerts.Add(new DashboardAlertViewModel
            {
                Title = "Yedekleme",
                Message = lastBackupAt is null
                    ? "Henüz yedek alınmamış."
                    : $"Son yedek {lastBackupAt.Value:dd.MM.yyyy HH:mm} tarihinde alınmış.",
                Severity = "warning",
                Icon = "backup",
                Url = Url.Action("Index", "Backups"),
                LinkText = "Yedekleri aç"
            });
        }

        var upcomingExpenses = expenseForecast.Where(x => x.Name != "Diğer").Take(5).Select((x, index) => new DashboardUpcomingExpense
        {
            Name = x.Name,
            Amount = x.Amount,
            Date = monthStart.AddDays(Math.Min(27, 5 + index * 5)),
            Status = index == 4 ? "Planlandı" : "Bekliyor"
        }).ToList();

        var vm = new DashboardViewModel
        {
            SelectedPeriod = selectedPeriod,
            PeriodOptions = DuesController.BuildPeriodOptions(periods, selectedPeriod),
            CollectionRate = collectionRate,
            CollectedInPeriod = collectedInPeriod,
            TotalGenerated = totalGenerated,
            OverdueDebt = overdueDebt,
            OverdueUnitCount = overdueUnitCount,
            OverdueCarriedDebt = overdueCarriedDebt,
            OverdueDuesDebt = overdueDuesDebt,
            ForecastExpense = forecastExpense,
            MonthCollections = monthCollections,
            MonthCollectionCount = monthCollectionCount,
            CashBankBalance = cashBankItems.Sum(x => x.Balance),
            NetPosition = monthCollections - forecastExpense,
            ActiveUnits = await db.Units.CountAsync(x => x.Active),
            OpenRequestCount = await db.ServiceRequests.CountAsync(x => x.Status == ServiceRequestStatus.Open || x.Status == ServiceRequestStatus.InProgress),
            ForecastConfidence = forecast.Confidence,
            ForecastMonthLabel = monthStart.ToString("MMMM yyyy"),
            ExpenseForecast = expenseForecast,
            Cashflow = await BuildCashflowAsync(monthStart),
            OverdueItems = overdueItems,
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
            CalendarDays = BuildCalendarDays(todayUtc),
            Alerts = alerts,
            LastBackupAt = lastBackupAt
        };

        return View(vm);
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
}
