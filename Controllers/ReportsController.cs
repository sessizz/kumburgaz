using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Kumburgaz.Web.Controllers;

[ModuleAuthorize(AppModules.Raporlar)]
public class ReportsController(
    ApplicationDbContext db,
    IReportingService reportingService,
    BalanceDetailedReportService balanceDetailedReportService,
    UnitLedgerService unitLedgerService) : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public async Task<IActionResult> CashBankStatement([FromQuery] CashBankStatementQuery query)
    {
        var accountOptions = await FinancialAccountHelper.BuildOptionsAsync(db, query.AccountKey);
        if (string.IsNullOrWhiteSpace(query.AccountKey) && accountOptions.Count > 0)
        {
            query.AccountKey = accountOptions[0].Value;
            accountOptions[0].Selected = true;
        }

        var model = new CashBankStatementViewModel
        {
            Query = query,
            AccountOptions = accountOptions
        };

        if (!FinancialAccountHelper.TryParse(query.AccountKey, out _, out var cashBoxId, out var bankAccountId))
        {
            return View(model);
        }

        var rows = await BuildCashBankRowsAsync(cashBoxId, bankAccountId);
        var accountInfo = await GetFinancialAccountInfoAsync(cashBoxId, bankAccountId);
        if (accountInfo is null)
        {
            return NotFound();
        }

        var start = query.StartDate?.Date;
        var endExclusive = query.EndDate?.Date.AddDays(1);
        var openingBalance = rows
            .Where(x => start.HasValue && x.Date < start.Value)
            .Sum(x => x.Amount);

        var statementRows = rows
            .Where(x => !start.HasValue || x.Date >= start.Value)
            .Where(x => !endExclusive.HasValue || x.Date < endExclusive.Value)
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Id)
            .ToList();

        var running = openingBalance;
        foreach (var row in statementRows)
        {
            running += row.Amount;
            row.RunningBalance = running;
        }

        model.AccountName = accountInfo.Value.Name;
        model.OpeningBalance = openingBalance;
        model.ClosingBalance = running;
        model.Rows = statementRows
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .Select(x => new CashBankStatementRow
            {
                Date = x.Date,
                Type = x.Type,
                Description = x.Description,
                Amount = x.Amount,
                RunningBalance = x.RunningBalance
            })
            .ToList();

        return View(model);
    }

    public async Task<IActionResult> Balance([FromQuery] BalanceReportQuery query)
    {
        var today = DateTime.Today;
        query.StartDate ??= new DateTime(today.Year, today.Month, 1);
        query.EndDate ??= today;

        var start = query.StartDate.Value.Date;
        var endExclusive = query.EndDate.Value.Date.AddDays(1);
        var startUtc = DateTimeHelper.EnsureUtc(start);
        var endExclusiveUtc = DateTimeHelper.EnsureUtc(endExclusive);

        var cashRows = await BuildCashBankRowsAsync(null, null);
        var openingCash = cashRows
            .Where(x => x.AccountKind == "cash" && x.Date < start)
            .Sum(x => x.Amount);
        var openingBank = cashRows
            .Where(x => x.AccountKind == "bank" && x.Date < start)
            .Sum(x => x.Amount);
        var closingCash = cashRows
            .Where(x => x.AccountKind == "cash" && x.Date < endExclusive)
            .Sum(x => x.Amount);
        var closingBank = cashRows
            .Where(x => x.AccountKind == "bank" && x.Date < endExclusive)
            .Sum(x => x.Amount);

        var collectionsByCategory = await db.Collections
            .AsNoTracking()
            .Where(x => x.Date >= startUtc && x.Date < endExclusiveUtc)
            .GroupBy(_ => "Aidat Tahsilatı")
            .Select(g => new BalanceCategoryTotal
            {
                CategoryName = g.Key,
                Amount = g.Sum(x => x.Amount)
            })
            .ToListAsync();

        var ledgerIncomeRows = await db.LedgerTransactions
            .AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Where(x => !x.IsTransfer && x.IncomeExpenseCategory != null && x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gelir)
            .Where(x => x.Date >= startUtc && x.Date < endExclusiveUtc)
            .GroupBy(x => x.IncomeExpenseCategory!.Name)
            .Select(g => new BalanceCategoryTotal
            {
                CategoryName = g.Key,
                Amount = g.Sum(x => x.Amount)
            })
            .ToListAsync();

        var expenseRows = await db.LedgerTransactions
            .AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Where(x => !x.IsTransfer && x.IncomeExpenseCategory != null && x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gider)
            .Where(x => x.Date >= startUtc && x.Date < endExclusiveUtc)
            .GroupBy(x => x.IncomeExpenseCategory!.Name)
            .Select(g => new BalanceCategoryTotal
            {
                CategoryName = g.Key,
                Amount = g.Sum(x => x.Amount)
            })
            .ToListAsync();

        var duesRows = await reportingService.GetDuesDebtReportAsync(new DuesDebtReportQuery());
        var carriedDebt = duesRows.Where(x => x.RemainingAmount > 0).Sum(x => x.RemainingAmount);
        var carriedCredit = duesRows.Where(x => x.RemainingAmount < 0).Sum(x => Math.Abs(x.RemainingAmount));

        return View("Balance", new BalanceReportViewModel
        {
            Query = query,
            OpeningCash = openingCash,
            OpeningBank = openingBank,
            ClosingCash = closingCash,
            ClosingBank = closingBank,
            IncomeRows = collectionsByCategory.Concat(ledgerIncomeRows)
                .GroupBy(x => x.CategoryName)
                .Select(g => new BalanceCategoryTotal
                {
                    CategoryName = g.Key,
                    Amount = g.Sum(x => x.Amount),
                    DelayAmount = g.Sum(x => x.DelayAmount)
                })
                .OrderBy(x => x.CategoryName)
                .ToList(),
            ExpenseRows = expenseRows.OrderBy(x => x.CategoryName).ToList(),
            CarriedDebt = carriedDebt,
            CarriedCredit = carriedCredit
        });
    }

    public async Task<IActionResult> IncomeExpenseSummary([FromQuery] BalanceReportQuery query)
    {
        ViewData["TitleOverride"] = "Gelir/Gider Özeti";
        return await Balance(query);
    }

    public async Task<IActionResult> MonthlyCashFlow([FromQuery] BalanceReportQuery query)
    {
        var today = DateTime.Today;
        query.EndDate ??= today;
        query.StartDate ??= new DateTime(today.Year, today.Month, 1).AddMonths(-11);

        var start = query.StartDate.Value.Date;
        var end = query.EndDate.Value.Date;
        var startUtc = DateTimeHelper.EnsureUtc(start);
        var endExclusiveUtc = DateTimeHelper.EnsureUtc(end.AddDays(1));

        var collectionRows = await db.Collections
            .AsNoTracking()
            .Where(x => x.Date >= startUtc && x.Date < endExclusiveUtc)
            .Select(x => new { x.Date, x.Amount })
            .ToListAsync();

        var ledgerRows = await db.LedgerTransactions
            .AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Where(x => !x.IsTransfer && x.Date >= startUtc && x.Date < endExclusiveUtc)
            .Select(x => new
            {
                x.Date,
                x.Amount,
                CategoryType = x.IncomeExpenseCategory == null ? CategoryTypeHelper.Gider : x.IncomeExpenseCategory.Type
            })
            .ToListAsync();

        var monthCursor = new DateTime(start.Year, start.Month, 1);
        var lastMonth = new DateTime(end.Year, end.Month, 1);
        var rows = new List<MonthlyCashFlowRow>();
        while (monthCursor <= lastMonth)
        {
            var month = monthCursor;
            rows.Add(new MonthlyCashFlowRow
            {
                Month = month,
                DuesCollections = collectionRows
                    .Where(x => x.Date.Year == month.Year && x.Date.Month == month.Month)
                    .Sum(x => x.Amount),
                OtherIncome = ledgerRows
                    .Where(x => x.Date.Year == month.Year && x.Date.Month == month.Month)
                    .Where(x => CategoryTypeHelper.Normalize(x.CategoryType) == CategoryTypeHelper.Gelir)
                    .Sum(x => x.Amount),
                Expense = ledgerRows
                    .Where(x => x.Date.Year == month.Year && x.Date.Month == month.Month)
                    .Where(x => CategoryTypeHelper.Normalize(x.CategoryType) == CategoryTypeHelper.Gider)
                    .Sum(x => x.Amount)
            });

            monthCursor = monthCursor.AddMonths(1);
        }

        return View(new MonthlyCashFlowViewModel
        {
            Query = query,
            Rows = rows
        });
    }

    public async Task<IActionResult> DebtAging([FromQuery] DuesDebtReportQuery query)
    {
        await PopulateFiltersAsync(query);

        var asOf = DateTime.Today;
        var unitQuery = db.Units
            .AsNoTracking()
            .Include(x => x.Block)
            .Include(x => x.UnitAccounts.Where(ua => ua.Active && ua.Role == UnitAccountRole.Owner))
            .ThenInclude(x => x.Account)
            .Where(x => x.Active);

        if (query.BlockId.HasValue)
        {
            unitQuery = unitQuery.Where(x => x.BlockId == query.BlockId.Value);
        }

        if (query.BillingGroupId.HasValue)
        {
            unitQuery = unitQuery.Where(x => x.BillingGroupUnits.Any(bg => bg.BillingGroupId == query.BillingGroupId.Value));
        }

        if (query.DuesTypeId.HasValue)
        {
            unitQuery = unitQuery.Where(x => x.BillingGroupUnits.Any(bg => bg.BillingGroup != null && bg.BillingGroup.DuesTypeId == query.DuesTypeId.Value));
        }

        var units = await unitQuery
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .ToListAsync();
        var summaries = await unitLedgerService.BuildSummariesAsync(units.Select(x => x.Id));
        var rows = new List<DebtAgingRow>();

        foreach (var unit in units)
        {
            if (!summaries.TryGetValue(unit.Id, out var summary))
            {
                continue;
            }

            var row = new DebtAgingRow
            {
                UnitId = unit.Id,
                UnitDisplay = UnitDisplayHelper.Display(unit),
                ResponsibleAccountName = unit.UnitAccounts
                    .OrderByDescending(x => x.StartDate ?? DateTime.MinValue)
                    .Select(x => x.Account?.Name)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? unit.OwnerName ?? string.Empty,
                Credit = summary.Advance
            };

            var remainingDebt = summary.Debt;
            if (remainingDebt > 0)
            {
                var installmentQuery = db.DuesInstallments
                    .AsNoTracking()
                    .Include(x => x.BillingGroup)
                    .Where(x => x.UnitId == unit.Id && x.RemainingAmount > 0);

                if (query.BillingGroupId.HasValue)
                {
                    installmentQuery = installmentQuery.Where(x => x.BillingGroupId == query.BillingGroupId.Value);
                }

                if (query.DuesTypeId.HasValue)
                {
                    installmentQuery = installmentQuery.Where(x => x.BillingGroup != null && x.BillingGroup.DuesTypeId == query.DuesTypeId.Value);
                }

                var installments = await installmentQuery
                    .OrderBy(x => x.DueDate)
                    .ThenBy(x => x.Id)
                    .ToListAsync();

                foreach (var installment in installments)
                {
                    if (remainingDebt <= 0)
                    {
                        break;
                    }

                    var amount = Math.Min(remainingDebt, installment.RemainingAmount);
                    ApplyAgingBucket(row, amount, installment.DueDate, asOf);
                    remainingDebt -= amount;
                }

                if (remainingDebt > 0)
                {
                    row.Over90 += remainingDebt;
                }
            }

            if (query.BalanceStatus?.Equals("debt", StringComparison.OrdinalIgnoreCase) == true && row.TotalDebt <= 0)
            {
                continue;
            }

            if (query.BalanceStatus?.Equals("credit", StringComparison.OrdinalIgnoreCase) == true && row.Credit <= 0)
            {
                continue;
            }

            if (query.BalanceStatus?.Equals("clear", StringComparison.OrdinalIgnoreCase) == true &&
                (row.TotalDebt != 0 || row.Credit != 0))
            {
                continue;
            }

            rows.Add(row);
        }

        return View(new DebtAgingViewModel
        {
            Query = query,
            Rows = rows
        });
    }

    public async Task<IActionResult> Income([FromQuery] BalanceReportQuery query)
    {
        var ledgerQuery = db.LedgerTransactions
            .AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Where(x => !x.IsTransfer
                        && x.IncomeExpenseCategory != null
                        && x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gelir);

        if (query.StartDate.HasValue)
        {
            var start = DateTimeHelper.EnsureUtc(query.StartDate.Value.Date);
            ledgerQuery = ledgerQuery.Where(x => x.Date >= start);
        }

        if (query.EndDate.HasValue)
        {
            var end = DateTimeHelper.EnsureUtc(query.EndDate.Value.Date.AddDays(1));
            ledgerQuery = ledgerQuery.Where(x => x.Date < end);
        }

        var rows = await ledgerQuery
            .GroupBy(x => x.IncomeExpenseCategory!.Name)
            .Select(x => new BalanceCategoryTotal
            {
                CategoryName = x.Key,
                Amount = x.Sum(t => t.Amount)
            })
            .OrderByDescending(x => x.Amount)
            .ToListAsync();

        return View("Income", new BalanceReportViewModel
        {
            Query = query,
            IncomeRows = rows
        });
    }

    public async Task<IActionResult> BalanceDetailed([FromQuery] BalanceDetailedQuery query)
    {
        var model = await balanceDetailedReportService.BuildAsync(query);
        return View(model);
    }

    public async Task<IActionResult> BalanceDetailedLines()
    {
        var lines = await db.ReportLines.AsNoTracking()
            .Include(x => x.Categories)
            .ThenInclude(x => x.IncomeExpenseCategory)
            .OrderBy(x => x.Section)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();

        var model = lines.Select(x => new ReportLineListItemViewModel
        {
            Id = x.Id,
            Name = x.Name,
            Section = CategoryTypeHelper.Normalize(x.Section),
            SortOrder = x.SortOrder,
            Visible = x.Visible,
            MembersText = string.Join(", ", x.Categories.Select(c => c.IsDuesCollections
                ? BalanceDetailedReportService.DuesCollectionsLabel
                : c.IncomeExpenseCategory?.Name ?? "?"))
        }).ToList();

        return View(model);
    }

    public async Task<IActionResult> BalanceDetailedLineCreate()
    {
        var model = new ReportLineFormViewModel
        {
            SortOrder = (await db.ReportLines.MaxAsync(x => (int?)x.SortOrder) ?? 0) + 10
        };
        await PopulateReportLineOptionsAsync(model);
        return View("BalanceDetailedLineForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BalanceDetailedLineCreate(ReportLineFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateReportLineOptionsAsync(model);
            return View("BalanceDetailedLineForm", model);
        }

        var line = new ReportLine();
        await SaveReportLineAsync(line, model, isNew: true);
        TempData["Success"] = $"'{line.Name}' rapor satırı eklendi.";
        return RedirectToAction(nameof(BalanceDetailedLines));
    }

    public async Task<IActionResult> BalanceDetailedLineEdit(int id)
    {
        var line = await db.ReportLines.AsNoTracking()
            .Include(x => x.Categories)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (line is null)
        {
            return NotFound();
        }

        var model = new ReportLineFormViewModel
        {
            Id = line.Id,
            Name = line.Name,
            Section = CategoryTypeHelper.Normalize(line.Section),
            Visible = line.Visible,
            SortOrder = line.SortOrder,
            SelectedKeys = line.Categories
                .Select(x => x.IsDuesCollections ? "AIDAT" : $"C:{x.IncomeExpenseCategoryId}")
                .ToList()
        };
        await PopulateReportLineOptionsAsync(model);
        return View("BalanceDetailedLineForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BalanceDetailedLineEdit(ReportLineFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateReportLineOptionsAsync(model);
            return View("BalanceDetailedLineForm", model);
        }

        var line = await db.ReportLines
            .Include(x => x.Categories)
            .FirstOrDefaultAsync(x => x.Id == model.Id);
        if (line is null)
        {
            return NotFound();
        }

        await SaveReportLineAsync(line, model, isNew: false);
        TempData["Success"] = $"'{line.Name}' rapor satırı güncellendi.";
        return RedirectToAction(nameof(BalanceDetailedLines));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BalanceDetailedLineDelete(int id)
    {
        var line = await db.ReportLines.FirstOrDefaultAsync(x => x.Id == id);
        if (line is not null)
        {
            db.ReportLines.Remove(line);
            await db.SaveChangesAsync();
            TempData["Success"] = $"'{line.Name}' rapor satırı silindi; kategorileri raporda tekrar kendi adlarıyla görünür.";
        }

        return RedirectToAction(nameof(BalanceDetailedLines));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BalanceDetailedLineToggle(int id)
    {
        var line = await db.ReportLines.FirstOrDefaultAsync(x => x.Id == id);
        if (line is not null)
        {
            line.Visible = !line.Visible;
            await db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(BalanceDetailedLines));
    }

    public async Task<IActionResult> BalanceDetailedManualEntries()
    {
        var entries = await db.ReportManualEntries.AsNoTracking()
            .Include(x => x.ReportLine)
            .OrderBy(x => x.SortOrder)
            .ThenByDescending(x => x.EntryDate)
            .ThenBy(x => x.Name)
            .Select(x => new ReportManualEntryListItemViewModel
            {
                Id = x.Id,
                Name = x.Name,
                Section = CategoryTypeHelper.Normalize(x.Section),
                EntryDate = x.EntryDate,
                CashAmount = x.CashAmount,
                BankAmount = x.BankAmount,
                SortOrder = x.SortOrder,
                Visible = x.Visible,
                ReportLineName = x.ReportLine == null ? null : x.ReportLine.Name,
                Note = x.Note
            })
            .ToListAsync();

        return View(entries);
    }

    public async Task<IActionResult> BalanceDetailedManualEntryCreate()
    {
        var model = new ReportManualEntryFormViewModel
        {
            SortOrder = (await db.ReportManualEntries.MaxAsync(x => (int?)x.SortOrder) ?? 0) + 10
        };
        await PopulateManualEntryOptionsAsync(model);
        return View("BalanceDetailedManualEntryForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BalanceDetailedManualEntryCreate(ReportManualEntryFormViewModel model)
    {
        ValidateManualEntry(model);
        if (!ModelState.IsValid)
        {
            await PopulateManualEntryOptionsAsync(model);
            return View("BalanceDetailedManualEntryForm", model);
        }

        var entry = new ReportManualEntry();
        SaveManualEntry(entry, model);
        db.ReportManualEntries.Add(entry);
        await db.SaveChangesAsync();
        TempData["Success"] = $"'{entry.Name}' manuel kalemi eklendi.";
        return RedirectToAction(nameof(BalanceDetailedManualEntries));
    }

    public async Task<IActionResult> BalanceDetailedManualEntryEdit(int id)
    {
        var entry = await db.ReportManualEntries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (entry is null)
        {
            return NotFound();
        }

        var model = new ReportManualEntryFormViewModel
        {
            Id = entry.Id,
            Name = entry.Name,
            Section = CategoryTypeHelper.Normalize(entry.Section),
            EntryDate = entry.EntryDate,
            CashAmount = entry.CashAmount,
            BankAmount = entry.BankAmount,
            SortOrder = entry.SortOrder,
            Visible = entry.Visible,
            ReportLineId = entry.ReportLineId,
            Note = entry.Note
        };
        await PopulateManualEntryOptionsAsync(model);
        return View("BalanceDetailedManualEntryForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BalanceDetailedManualEntryEdit(ReportManualEntryFormViewModel model)
    {
        ValidateManualEntry(model);
        if (!ModelState.IsValid)
        {
            await PopulateManualEntryOptionsAsync(model);
            return View("BalanceDetailedManualEntryForm", model);
        }

        var entry = await db.ReportManualEntries.FirstOrDefaultAsync(x => x.Id == model.Id);
        if (entry is null)
        {
            return NotFound();
        }

        SaveManualEntry(entry, model);
        await db.SaveChangesAsync();
        TempData["Success"] = $"'{entry.Name}' manuel kalemi güncellendi.";
        return RedirectToAction(nameof(BalanceDetailedManualEntries));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BalanceDetailedManualEntryToggle(int id)
    {
        var entry = await db.ReportManualEntries.FirstOrDefaultAsync(x => x.Id == id);
        if (entry is not null)
        {
            entry.Visible = !entry.Visible;
            await db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(BalanceDetailedManualEntries));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BalanceDetailedManualEntryDelete(int id)
    {
        var entry = await db.ReportManualEntries.FirstOrDefaultAsync(x => x.Id == id);
        if (entry is not null)
        {
            db.ReportManualEntries.Remove(entry);
            await db.SaveChangesAsync();
            TempData["Success"] = $"'{entry.Name}' manuel kalemi silindi.";
        }

        return RedirectToAction(nameof(BalanceDetailedManualEntries));
    }

    private async Task SaveReportLineAsync(ReportLine line, ReportLineFormViewModel model, bool isNew)
    {
        line.Name = model.Name.Trim();
        line.Section = CategoryTypeHelper.Normalize(model.Section);
        line.Visible = model.Visible;
        line.SortOrder = model.SortOrder;

        var wantsDues = model.SelectedKeys.Contains("AIDAT");
        var categoryIds = model.SelectedKeys
            .Where(x => x.StartsWith("C:"))
            .Select(x => int.TryParse(x[2..], out var id) ? id : 0)
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        // Bir kategori tek satırda olabilir: seçilenler başka satırlardaysa oradan taşınır.
        var conflicting = await db.ReportLineCategories
            .Where(x => x.ReportLineId != line.Id &&
                        ((x.IncomeExpenseCategoryId != null && categoryIds.Contains(x.IncomeExpenseCategoryId.Value)) ||
                         (wantsDues && x.IsDuesCollections)))
            .ToListAsync();
        db.ReportLineCategories.RemoveRange(conflicting);
        db.ReportLineCategories.RemoveRange(line.Categories);

        line.Categories = categoryIds
            .Select(id => new ReportLineCategory { IncomeExpenseCategoryId = id })
            .ToList();
        if (wantsDues)
        {
            line.Categories.Add(new ReportLineCategory { IsDuesCollections = true });
        }

        if (isNew)
        {
            db.ReportLines.Add(line);
        }

        await db.SaveChangesAsync();
    }

    private void ValidateManualEntry(ReportManualEntryFormViewModel model)
    {
        var section = CategoryTypeHelper.Normalize(model.Section);
        if (section is not (CategoryTypeHelper.Gelir or CategoryTypeHelper.Gider))
        {
            ModelState.AddModelError(nameof(ReportManualEntryFormViewModel.Section), "Bölüm Gelir veya Gider olmalıdır.");
        }

        if (model.CashAmount == 0 && model.BankAmount == 0)
        {
            ModelState.AddModelError(string.Empty, "Kasa veya banka tutarından en az biri girilmelidir.");
        }
    }

    private void SaveManualEntry(ReportManualEntry entry, ReportManualEntryFormViewModel model)
    {
        entry.Name = model.Name.Trim();
        entry.Section = CategoryTypeHelper.Normalize(model.Section);
        entry.EntryDate = DateTimeHelper.EnsureUtc(model.EntryDate.Date);
        entry.CashAmount = model.CashAmount;
        entry.BankAmount = model.BankAmount;
        entry.SortOrder = model.SortOrder;
        entry.Visible = model.Visible;
        entry.ReportLineId = model.ReportLineId;
        entry.Note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim();
    }

    private async Task PopulateManualEntryOptionsAsync(ReportManualEntryFormViewModel model)
    {
        model.ReportLineOptions = await db.ReportLines.AsNoTracking()
            .OrderBy(x => x.Section)
            .ThenBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new SelectListItem($"{CategoryTypeHelper.Display(x.Section)} - {x.Name}", x.Id.ToString(), model.ReportLineId == x.Id))
            .ToListAsync();
    }

    private async Task PopulateReportLineOptionsAsync(ReportLineFormViewModel model)
    {
        var assignments = await db.ReportLineCategories.AsNoTracking()
            .Include(x => x.ReportLine)
            .Where(x => model.Id == null || x.ReportLineId != model.Id)
            .ToListAsync();
        var byCategory = assignments
            .Where(x => x.IncomeExpenseCategoryId != null)
            .ToDictionary(x => x.IncomeExpenseCategoryId!.Value, x => x.ReportLine?.Name);
        var duesLine = assignments.FirstOrDefault(x => x.IsDuesCollections)?.ReportLine?.Name;

        var categories = await db.IncomeExpenseCategories.AsNoTracking()
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .ToListAsync();

        model.Options =
        [
            new ReportLineCategoryOption
            {
                Key = "AIDAT",
                Label = BalanceDetailedReportService.DuesCollectionsLabel,
                Type = CategoryTypeHelper.Gelir,
                CurrentLineName = duesLine
            },
            .. categories.Select(x => new ReportLineCategoryOption
            {
                Key = $"C:{x.Id}",
                Label = x.Name,
                Type = CategoryTypeHelper.Normalize(x.Type),
                CurrentLineName = byCategory.GetValueOrDefault(x.Id)
            })
        ];
    }

    public async Task<IActionResult> DuesDebt([FromQuery] DuesDebtReportQuery query)
    {
        await PopulateFiltersAsync(query);
        var rows = await reportingService.GetDuesDebtReportAsync(query);
        ViewBag.Query = query;
        return View(rows);
    }

    public async Task<IActionResult> DuesDebtExcel([FromQuery] DuesDebtReportQuery query)
    {
        var rows = await reportingService.GetDuesDebtReportAsync(query);
        var bytes = reportingService.ExportDuesDebtAsExcel(rows, query);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "aidat-borc-raporu.xlsx");
    }

    public async Task<IActionResult> DuesDebtPdf([FromQuery] DuesDebtReportQuery query)
    {
        var rows = await reportingService.GetDuesDebtReportAsync(query);
        var bytes = reportingService.ExportDuesDebtAsPdf(rows, query);
        return File(bytes, "application/pdf", "aidat-borc-raporu.pdf");
    }

    public async Task<IActionResult> Attendance([FromQuery] AttendanceReportQuery query)
    {
        await PopulateAttendanceFiltersAsync(query);
        var model = await reportingService.GetAttendanceReportAsync(query);
        return View(model);
    }

    public async Task<IActionResult> AttendanceExcel([FromQuery] AttendanceReportQuery query)
    {
        var model = await reportingService.GetAttendanceReportAsync(query);
        var bytes = reportingService.ExportAttendanceAsExcel(model);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "hazirun-cetveli.xlsx");
    }

    public async Task<IActionResult> AttendancePdf([FromQuery] AttendanceReportQuery query)
    {
        var model = await reportingService.GetAttendanceReportAsync(query);
        var bytes = reportingService.ExportAttendanceAsPdf(model);
        return File(bytes, "application/pdf", "hazirun-cetveli.pdf");
    }

    public async Task<IActionResult> DuesStatus([FromQuery] DuesDebtReportQuery query)
    {
        await PopulateFiltersAsync(query);
        var rows = await reportingService.GetDuesDebtReportAsync(query);
        var model = reportingService.BuildDuesStatusReport(rows, query);
        return View(model);
    }

    public async Task<IActionResult> DuesStatusExcel([FromQuery] DuesDebtReportQuery query)
    {
        var rows = await reportingService.GetDuesDebtReportAsync(query);
        var model = reportingService.BuildDuesStatusReport(rows, query);
        var bytes = reportingService.ExportDuesStatusAsExcel(model);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "aidat-durum-cetveli.xlsx");
    }

    public async Task<IActionResult> DuesStatusPdf([FromQuery] DuesDebtReportQuery query)
    {
        var rows = await reportingService.GetDuesDebtReportAsync(query);
        var model = reportingService.BuildDuesStatusReport(rows, query);
        var bytes = reportingService.ExportDuesStatusAsPdf(model);
        return File(bytes, "application/pdf", "aidat-durum-cetveli.pdf");
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

    private static void ApplyAgingBucket(DebtAgingRow row, decimal amount, DateTime dueDate, DateTime asOf)
    {
        var daysOverdue = (asOf.Date - dueDate.Date).Days;
        if (daysOverdue <= 0)
        {
            row.Current += amount;
        }
        else if (daysOverdue <= 30)
        {
            row.Days1To30 += amount;
        }
        else if (daysOverdue <= 60)
        {
            row.Days31To60 += amount;
        }
        else if (daysOverdue <= 90)
        {
            row.Days61To90 += amount;
        }
        else
        {
            row.Over90 += amount;
        }
    }

    private async Task PopulateFiltersAsync(DuesDebtReportQuery duesQuery)
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

        ViewBag.BalanceStatuses = new List<SelectListItem>
        {
            new("Borçlular", "debt", string.Equals(duesQuery.BalanceStatus, "debt", StringComparison.OrdinalIgnoreCase)),
            new("Alacaklılar", "credit", string.Equals(duesQuery.BalanceStatus, "credit", StringComparison.OrdinalIgnoreCase)),
            new("Bakiyesizler", "clear", string.Equals(duesQuery.BalanceStatus, "clear", StringComparison.OrdinalIgnoreCase))
        };
    }

    private async Task PopulateAttendanceFiltersAsync(AttendanceReportQuery query)
    {
        ViewBag.Blocks = await db.Blocks
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString(), query.BlockId == x.Id))
            .ToListAsync();
    }

    private async Task<(string Name, decimal OpeningBalance, DateTime OpeningDate)?> GetFinancialAccountInfoAsync(int? cashBoxId, int? bankAccountId)
    {
        if (cashBoxId.HasValue)
        {
            var cash = await db.CashBoxes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cashBoxId.Value);
            return cash is null ? null : (cash.Name, cash.OpeningBalance, cash.OpeningBalanceDate);
        }

        if (bankAccountId.HasValue)
        {
            var bank = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == bankAccountId.Value);
            if (bank is null)
            {
                return null;
            }

            var name = string.IsNullOrWhiteSpace(bank.Branch) ? bank.Name : $"{bank.Name} / {bank.Branch}";
            return (name, bank.OpeningBalance, bank.OpeningBalanceDate);
        }

        return null;
    }

    private async Task<List<StatementSourceRow>> BuildCashBankRowsAsync(int? cashBoxId, int? bankAccountId)
    {
        var rows = new List<StatementSourceRow>();

        if (cashBoxId.HasValue || (!cashBoxId.HasValue && !bankAccountId.HasValue))
        {
            var cashBoxes = await db.CashBoxes
                .AsNoTracking()
                .Where(x => !cashBoxId.HasValue || x.Id == cashBoxId.Value)
                .ToListAsync();
            rows.AddRange(cashBoxes.Select(x => new StatementSourceRow
            {
                Id = 0,
                AccountKind = "cash",
                AccountId = x.Id,
                Date = x.OpeningBalanceDate,
                Type = "Açılış",
                Description = $"{x.Name} açılış bakiyesi",
                Amount = x.OpeningBalance
            }));
        }

        if (bankAccountId.HasValue || (!cashBoxId.HasValue && !bankAccountId.HasValue))
        {
            var bankAccounts = await db.BankAccounts
                .AsNoTracking()
                .Where(x => !bankAccountId.HasValue || x.Id == bankAccountId.Value)
                .ToListAsync();
            rows.AddRange(bankAccounts.Select(x => new StatementSourceRow
            {
                Id = 0,
                AccountKind = "bank",
                AccountId = x.Id,
                Date = x.OpeningBalanceDate,
                Type = "Açılış",
                Description = $"{x.Name} açılış bakiyesi",
                Amount = x.OpeningBalance
            }));
        }

        var collections = await db.Collections
            .AsNoTracking()
            .Include(x => x.BillingGroup)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Where(x => !cashBoxId.HasValue || x.CashBoxId == cashBoxId.Value)
            .Where(x => !bankAccountId.HasValue || x.BankAccountId == bankAccountId.Value)
            .ToListAsync();
        rows.AddRange(collections.Select(x => new StatementSourceRow
        {
            Id = x.Id,
            AccountKind = x.CashBoxId.HasValue ? "cash" : "bank",
            AccountId = x.CashBoxId ?? x.BankAccountId ?? 0,
            Date = x.Date,
            Type = "Tahsilat",
            Description = $"{x.BillingGroup?.Name ?? "Aidat"} - {UnitDisplayHelper.Display(x.Unit)}",
            Amount = x.Amount
        }));

        var ledgerRows = await db.LedgerTransactions
            .AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Include(x => x.CashBox)
            .Include(x => x.BankAccount)
            .Where(x => !cashBoxId.HasValue || x.CashBoxId == cashBoxId.Value)
            .Where(x => !bankAccountId.HasValue || x.BankAccountId == bankAccountId.Value)
            .ToListAsync();
        rows.AddRange(ledgerRows.Select(x =>
        {
            var signedAmount = SignedLedgerAmount(x);
            return new StatementSourceRow
            {
                Id = x.Id,
                AccountKind = x.CashBoxId.HasValue ? "cash" : "bank",
                AccountId = x.CashBoxId ?? x.BankAccountId ?? 0,
                Date = x.Date,
                Type = x.IsTransfer ? "Transfer" : signedAmount >= 0 ? "Gelir" : "Gider",
                Description = x.Description ?? x.IncomeExpenseCategory?.Name ?? "Muhasebe fişi",
                Amount = signedAmount
            };
        }));

        return rows;
    }

    private static decimal SignedLedgerAmount(LedgerTransaction transaction)
    {
        if (transaction.IsTransfer)
        {
            return transaction.TransferIsIncoming ? transaction.Amount : -transaction.Amount;
        }

        return transaction.IncomeExpenseCategory?.Type == CategoryTypeHelper.Gelir
            ? transaction.Amount
            : -transaction.Amount;
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

    private sealed class StatementSourceRow
    {
        public int Id { get; set; }
        public string AccountKind { get; set; } = string.Empty;
        public int AccountId { get; set; }
        public DateTime Date { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal RunningBalance { get; set; }
    }
}
