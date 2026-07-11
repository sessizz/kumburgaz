using ClosedXML.Excel;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

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

        var (carryCash, carryBank) = await GetCarryOverAsync(startUtc);
        if (carryCash != 0 || carryBank != 0)
        {
            model.IncomeRows.Add(new BalanceDetailedRow
            {
                Name = "Devreden Bakiye (Geçen Dönem)",
                SortOrder = -1_000_000,
                Cash = carryCash,
                Bank = carryBank,
                IsCarryOver = true,
                MembersText = "Filtrelenen başlangıç tarihinden önceki kasa/banka bakiyesi"
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

    /// <summary>
    /// Başlangıç tarihinden önce kasa/bankada birikmiş bakiyeyi (devir) hesaplar:
    /// kasa/banka açılış bakiyeleri + o tarihten önceki tüm tahsilat ve muhasebe fişleri (transferler dahil).
    /// </summary>
    private async Task<(decimal Cash, decimal Bank)> GetCarryOverAsync(DateTime startUtc)
    {
        var cash = await db.CashBoxes.AsNoTracking()
            .Where(x => x.OpeningBalanceDate < startUtc)
            .SumAsync(x => (decimal?)x.OpeningBalance) ?? 0m;
        var bank = await db.BankAccounts.AsNoTracking()
            .Where(x => x.OpeningBalanceDate < startUtc)
            .SumAsync(x => (decimal?)x.OpeningBalance) ?? 0m;

        var collections = await db.Collections.AsNoTracking()
            .Where(x => x.Date < startUtc)
            .GroupBy(x => x.CashBoxId != null)
            .Select(g => new { IsCash = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToListAsync();
        cash += collections.Where(x => x.IsCash).Sum(x => x.Amount);
        bank += collections.Where(x => !x.IsCash).Sum(x => x.Amount);

        var ledgerRows = await db.LedgerTransactions.AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Where(x => x.Date < startUtc)
            .ToListAsync();
        foreach (var tx in ledgerRows)
        {
            var signed = SignedLedgerAmount(tx);
            if (tx.CashBoxId.HasValue)
            {
                cash += signed;
            }
            else
            {
                bank += signed;
            }
        }

        return (cash, bank);
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

    public byte[] ExportAsExcel(BalanceDetailedViewModel model)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Bilanço Detaylı");

        var r = 1;
        ws.Cell(r, 1).Value = "Bilanço Detaylı Raporu";
        ws.Range(r, 1, r, 4).Merge().Style.Font.SetBold().Font.SetFontSize(14);
        r++;
        ws.Cell(r, 1).Value = BuildFilterSummary(model.Query);
        ws.Range(r, 1, r, 4).Merge().Style.Font.FontSize = 9;
        r += 2;

        r = WriteSection(ws, r, "GELİRLER", model.IncomeRows, model.IncomeCash, model.IncomeBank, model.IncomeTotal);
        r++;
        r = WriteSection(ws, r, "GİDERLER", model.ExpenseRows, model.ExpenseCash, model.ExpenseBank, model.ExpenseTotal);
        r += 2;

        ws.Cell(r, 1).Value = "Gelir - Gider Farkı";
        ws.Cell(r, 1).Style.Font.Bold = true;
        ws.Cell(r, 4).Value = model.Net;
        ws.Cell(r, 4).Style.Font.Bold = true;

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static int WriteSection(IXLWorksheet ws, int r, string title, List<BalanceDetailedRow> rows, decimal cash, decimal bank, decimal total)
    {
        ws.Cell(r, 1).Value = title;
        ws.Range(r, 1, r, 4).Style.Font.SetBold().Font.SetFontSize(12);
        r++;

        ws.Cell(r, 1).Value = "Çeşit";
        ws.Cell(r, 2).Value = "Kasadan";
        ws.Cell(r, 3).Value = "Bankadan";
        ws.Cell(r, 4).Value = "Toplam";
        ws.Range(r, 1, r, 4).Style.Font.Bold = true;
        r++;

        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.IsCarryOver ? $"{row.Name} (Devir)" : row.Name;
            ws.Cell(r, 2).Value = row.Cash;
            ws.Cell(r, 3).Value = row.Bank;
            ws.Cell(r, 4).Value = row.Total;
            r++;
        }

        ws.Cell(r, 1).Value = $"{title} Toplamı";
        ws.Cell(r, 2).Value = cash;
        ws.Cell(r, 3).Value = bank;
        ws.Cell(r, 4).Value = total;
        ws.Range(r, 1, r, 4).Style.Font.Bold = true;
        r++;

        return r;
    }

    public byte[] ExportAsPdf(BalanceDetailedViewModel model)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Portrait());
                page.Margin(24);
                page.Header().Column(header =>
                {
                    header.Item().Text("Bilanço Detaylı Raporu").FontSize(16).Bold();
                    header.Item().Text(BuildFilterSummary(model.Query)).FontSize(9);
                });
                page.Content().Column(column =>
                {
                    column.Spacing(10);
                    column.Item().Element(c => WritePdfSection(c, "GELİRLER", model.IncomeRows, model.IncomeCash, model.IncomeBank, model.IncomeTotal));
                    column.Item().Element(c => WritePdfSection(c, "GİDERLER", model.ExpenseRows, model.ExpenseCash, model.ExpenseBank, model.ExpenseTotal));
                    column.Item().PaddingTop(6).Row(row =>
                    {
                        row.RelativeItem().Text("Gelir - Gider Farkı").Bold();
                        row.ConstantItem(100).AlignRight().Text(model.Net.ToString("N2")).Bold();
                    });
                });
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Sayfa ");
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    private static void WritePdfSection(IContainer container, string title, List<BalanceDetailedRow> rows, decimal cash, decimal bank, decimal total)
    {
        container.Column(column =>
        {
            column.Item().Text(title).FontSize(12).Bold();
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(4);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                });

                table.Header(header =>
                {
                    header.Cell().Text("Çeşit").Bold();
                    header.Cell().AlignRight().Text("Kasadan").Bold();
                    header.Cell().AlignRight().Text("Bankadan").Bold();
                    header.Cell().AlignRight().Text("Toplam").Bold();
                });

                foreach (var row in rows)
                {
                    table.Cell().Text(row.IsCarryOver ? $"{row.Name} (Devir)" : row.Name);
                    table.Cell().AlignRight().Text(row.Cash.ToString("N2"));
                    table.Cell().AlignRight().Text(row.Bank.ToString("N2"));
                    table.Cell().AlignRight().Text(row.Total.ToString("N2"));
                }

                table.Cell().Text($"{title} Toplamı").Bold();
                table.Cell().AlignRight().Text(cash.ToString("N2")).Bold();
                table.Cell().AlignRight().Text(bank.ToString("N2")).Bold();
                table.Cell().AlignRight().Text(total.ToString("N2")).Bold();
            });
        });
    }

    private static string BuildFilterSummary(BalanceDetailedQuery query)
    {
        var start = query.StartDate?.ToString("dd.MM.yyyy") ?? "-";
        var end = query.EndDate?.ToString("dd.MM.yyyy") ?? "-";
        return $"Tarih Aralığı: {start} - {end}";
    }
}
