using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

/// <summary>
/// "Bilanço Detaylı" raporunu üretir. Kullanıcının tanımladığı rapor satırları (ReportLine)
/// bir veya birden çok gelir/gider kategorisini tek başlık altında toplar; karışık satırlarda
/// (örn. faiz geliri + faiz vergisi) karşıt tipteki üyeler net tutardan düşülür.
/// Hiçbir satıra atanmamış kategoriler kendi adlarıyla otomatik listelenir, kaybolmaz.
/// </summary>
public class BalanceDetailedReportService(ApplicationDbContext db)
{
    private sealed record SourceTotal(string Name, string Type, decimal Cash, decimal Bank);

    public const string DuesCollectionsLabel = "Aidat Tahsilatı";

    public async Task<BalanceDetailedViewModel> BuildAsync(BalanceDetailedQuery query)
    {
        var today = DateTime.Today;
        query.StartDate ??= new DateTime(today.Year, today.Month, 1);
        query.EndDate ??= today;

        var startUtc = DateTimeHelper.EnsureUtc(query.StartDate.Value.Date);
        var endExclusiveUtc = DateTimeHelper.EnsureUtc(query.EndDate.Value.Date.AddDays(1));

        // Kaynak tutarlar: kategori bazında kasa/banka kırılımı
        var ledger = await db.LedgerTransactions.AsNoTracking()
            .Where(x => !x.IsTransfer &&
                        x.IncomeExpenseCategory != null &&
                        x.Date >= startUtc && x.Date < endExclusiveUtc)
            .GroupBy(x => new
            {
                Id = x.IncomeExpenseCategoryId!.Value,
                x.IncomeExpenseCategory!.Name,
                x.IncomeExpenseCategory.Type,
                IsCash = x.CashBoxId != null
            })
            .Select(g => new { g.Key.Id, g.Key.Name, g.Key.Type, g.Key.IsCash, Amount = g.Sum(x => x.Amount) })
            .ToListAsync();

        var totalsByCategory = ledger
            .GroupBy(x => x.Id)
            .ToDictionary(
                g => g.Key,
                g => new SourceTotal(
                    g.First().Name,
                    CategoryTypeHelper.Normalize(g.First().Type),
                    g.Where(x => x.IsCash).Sum(x => x.Amount),
                    g.Where(x => !x.IsCash).Sum(x => x.Amount)));

        var collections = await db.Collections.AsNoTracking()
            .Where(x => x.Date >= startUtc && x.Date < endExclusiveUtc)
            .GroupBy(x => x.CashBoxId != null)
            .Select(g => new { IsCash = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToListAsync();
        var duesTotal = new SourceTotal(
            DuesCollectionsLabel,
            CategoryTypeHelper.Gelir,
            collections.Where(x => x.IsCash).Sum(x => x.Amount),
            collections.Where(x => !x.IsCash).Sum(x => x.Amount));

        var lines = await db.ReportLines.AsNoTracking()
            .Include(x => x.Categories)
            .ThenInclude(x => x.IncomeExpenseCategory)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync();

        var manualEntries = await db.ReportManualEntries.AsNoTracking()
            .Where(x => x.EntryDate >= startUtc && x.EntryDate < endExclusiveUtc)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.EntryDate)
            .ThenBy(x => x.Name)
            .ToListAsync();
        var visibleManualByLine = manualEntries
            .Where(x => x.Visible && x.ReportLineId.HasValue)
            .GroupBy(x => x.ReportLineId!.Value)
            .ToDictionary(x => x.Key, x => x.ToList());

        var model = new BalanceDetailedViewModel { Query = query };
        var assignedCategoryIds = new HashSet<int>();
        var duesAssigned = false;

        foreach (var line in lines)
        {
            var row = new BalanceDetailedRow { Name = line.Name, SortOrder = line.SortOrder };
            var memberNames = new List<string>();

            foreach (var member in line.Categories)
            {
                SourceTotal? source = null;
                if (member.IsDuesCollections)
                {
                    source = duesTotal;
                    duesAssigned = true;
                    memberNames.Add(DuesCollectionsLabel);
                }
                else if (member.IncomeExpenseCategoryId is { } catId)
                {
                    assignedCategoryIds.Add(catId);
                    totalsByCategory.TryGetValue(catId, out source);
                    memberNames.Add(member.IncomeExpenseCategory?.Name ?? source?.Name ?? "?");
                }

                if (source is null)
                {
                    continue;
                }

                // Satırın bölümüyle aynı tip toplanır, karşıt tip netten düşülür.
                var sign = source.Type == CategoryTypeHelper.Normalize(line.Section) ? 1m : -1m;
                row.Cash += sign * source.Cash;
                row.Bank += sign * source.Bank;
            }

            if (visibleManualByLine.TryGetValue(line.Id, out var lineManualEntries))
            {
                foreach (var entry in lineManualEntries)
                {
                    var sign = CategoryTypeHelper.Normalize(entry.Section) == CategoryTypeHelper.Normalize(line.Section) ? 1m : -1m;
                    row.Cash += sign * entry.CashAmount;
                    row.Bank += sign * entry.BankAmount;
                    memberNames.Add($"{entry.Name} (manuel)");
                }
            }

            row.MembersText = string.Join(", ", memberNames);

            if (!line.Visible)
            {
                model.HiddenLineCount++;
                model.HiddenTotal += row.Total;
                continue;
            }

            (CategoryTypeHelper.Normalize(line.Section) == CategoryTypeHelper.Gelir
                ? model.IncomeRows
                : model.ExpenseRows).Add(row);
        }

        foreach (var entry in manualEntries.Where(x => !x.Visible))
        {
            model.HiddenLineCount++;
            model.HiddenTotal += entry.CashAmount + entry.BankAmount;
        }

        foreach (var entry in manualEntries
                     .Where(x => x.Visible && !x.ReportLineId.HasValue)
                     .OrderBy(x => x.SortOrder)
                     .ThenBy(x => x.EntryDate)
                     .ThenBy(x => x.Name))
        {
            var row = new BalanceDetailedRow
            {
                Name = entry.Name,
                SortOrder = entry.SortOrder,
                Cash = entry.CashAmount,
                Bank = entry.BankAmount,
                IsManual = true,
                MembersText = "Manuel kalem",
                Note = entry.Note
            };

            (CategoryTypeHelper.Normalize(entry.Section) == CategoryTypeHelper.Gelir
                ? model.IncomeRows
                : model.ExpenseRows).Add(row);
        }

        // Atanmamış kaynaklar kendi adlarıyla listelenir
        if (!duesAssigned && (duesTotal.Cash != 0 || duesTotal.Bank != 0))
        {
            model.IncomeRows.Add(new BalanceDetailedRow
            {
                Name = DuesCollectionsLabel,
                SortOrder = 900000,
                Cash = duesTotal.Cash,
                Bank = duesTotal.Bank,
                IsAuto = true
            });
        }

        foreach (var (_, source) in totalsByCategory
                     .Where(x => !assignedCategoryIds.Contains(x.Key))
                     .OrderBy(x => x.Value.Name))
        {
            (source.Type == CategoryTypeHelper.Gelir ? model.IncomeRows : model.ExpenseRows).Add(
                new BalanceDetailedRow
                {
                    Name = source.Name,
                    SortOrder = 900000,
                    Cash = source.Cash,
                    Bank = source.Bank,
                    IsAuto = true
                });
        }

        model.IncomeRows = model.IncomeRows
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToList();
        model.ExpenseRows = model.ExpenseRows
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToList();

        return model;
    }
}
