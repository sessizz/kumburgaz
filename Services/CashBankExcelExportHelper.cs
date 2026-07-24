using ClosedXML.Excel;
using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Services;

/// <summary>
/// Kasa/Banka detay sayfasındaki işlemleri (aktif filtreye göre, sayfalama olmadan tümü)
/// biçimli bir Excel (.xlsx) dosyası olarak dışa aktarır.
/// </summary>
public static class CashBankExcelExportHelper
{
    private static readonly XLColor Brand = XLColor.FromHtml("#0D9488");      // teal-600
    private static readonly XLColor BrandDark = XLColor.FromHtml("#0F766E");  // teal-700
    private static readonly XLColor HeaderText = XLColor.White;
    private static readonly XLColor ZebraFill = XLColor.FromHtml("#F1F5F9");  // slate-100
    private static readonly XLColor Inflow = XLColor.FromHtml("#047857");     // green-700
    private static readonly XLColor Outflow = XLColor.FromHtml("#B91C1C");    // red-700
    private static readonly XLColor Muted = XLColor.FromHtml("#64748B");      // slate-500
    private static readonly XLColor GridLine = XLColor.FromHtml("#E2E8F0");   // slate-200
    private static readonly XLColor CardFill = XLColor.FromHtml("#F8FAFC");   // slate-50

    private const string MoneyFormat = "#,##0.00 ₺";
    private const int FirstColumn = 1;   // A
    private const int LastColumn = 8;    // H

    public static byte[] Build(CashBankDetailViewModel vm)
    {
        // Ekranda gösterilen (filtrelenmiş) tüm satırlar; ekstre gibi eskiden yeniye sıralı.
        var rows = vm.Groups
            .SelectMany(g => g.Items)
            .OrderBy(r => r.Date)
            .ThenBy(r => r.Id)
            .ToList();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SafeSheetName(vm.Name));
        ws.ShowGridLines = false;
        ws.Style.Font.FontName = "Calibri";
        ws.Style.Font.FontSize = 10;

        var row = BuildHeader(ws, vm, rows);
        row = BuildSummary(ws, vm, rows, row);
        BuildTable(ws, rows, row);

        for (var c = FirstColumn; c <= LastColumn; c++)
        {
            var column = ws.Column(c);
            column.AdjustToContents();
            // İçeriğe göre ölç ama makul sınırlar içinde tut (çok dar/çok geniş sütun olmasın).
            if (column.Width < 12) column.Width = 12;
            if (column.Width > 48) column.Width = 48;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static int BuildHeader(IXLWorksheet ws, CashBankDetailViewModel vm, List<TxRow> rows)
    {
        var title = vm.Kind == "bank" ? "BANKA HESAP HAREKETLERİ" : "KASA HAREKETLERİ";

        var titleRange = ws.Range(1, FirstColumn, 1, LastColumn).Merge();
        titleRange.FirstCell().Value = $"{title} — {vm.Name}";
        titleRange.Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(HeaderText);
        titleRange.Style.Fill.SetBackgroundColor(Brand);
        titleRange.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        titleRange.Style.Alignment.SetIndent(1);
        ws.Row(1).Height = 30;

        var subtitleParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(vm.Branch)) subtitleParts.Add($"Şube: {vm.Branch}");
        if (!string.IsNullOrWhiteSpace(vm.Iban)) subtitleParts.Add($"IBAN: {vm.Iban}");
        subtitleParts.Add($"Filtre: {DescribeFilter(vm.Query)}");
        subtitleParts.Add($"İşlem sayısı: {rows.Count(r => r.Kind != TxKind.Acilis)}");
        subtitleParts.Add($"Rapor tarihi: {DateTime.Now:dd.MM.yyyy HH:mm}");

        var subtitleRange = ws.Range(2, FirstColumn, 2, LastColumn).Merge();
        subtitleRange.FirstCell().Value = string.Join("   •   ", subtitleParts);
        subtitleRange.Style.Font.SetFontSize(9).Font.SetFontColor(HeaderText);
        subtitleRange.Style.Fill.SetBackgroundColor(BrandDark);
        subtitleRange.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        subtitleRange.Style.Alignment.SetIndent(1);
        ws.Row(2).Height = 20;

        return 4; // 3. satır boş bırakılır
    }

    private static int BuildSummary(IXLWorksheet ws, CashBankDetailViewModel vm, List<TxRow> rows, int startRow)
    {
        var totalInflow = rows.Where(r => r.Kind != TxKind.Acilis && r.Amount > 0).Sum(r => r.Amount);
        var totalOutflow = rows.Where(r => r.Kind != TxKind.Acilis && r.Amount < 0).Sum(r => Math.Abs(r.Amount));

        var cards = new (string Label, decimal Value, XLColor Color)[]
        {
            ("Açılış Bakiyesi", vm.OpeningBalance, Muted),
            ("Toplam Giriş", totalInflow, Inflow),
            ("Toplam Çıkış", totalOutflow, Outflow),
            ("Güncel Bakiye", vm.Balance, vm.Balance < 0 ? Outflow : BrandDark)
        };

        // Her kart 2 sütun kaplar: A-B, C-D, E-F, G-H
        for (var i = 0; i < cards.Length; i++)
        {
            var (label, value, color) = cards[i];
            var col = FirstColumn + i * 2;

            var labelCell = ws.Range(startRow, col, startRow, col + 1).Merge();
            labelCell.FirstCell().Value = label.ToUpperInvariant();
            labelCell.Style.Font.SetFontSize(8).Font.SetBold().Font.SetFontColor(Muted);
            labelCell.Style.Fill.SetBackgroundColor(CardFill);
            labelCell.Style.Alignment.SetIndent(1);
            labelCell.Style.Border.SetTopBorder(XLBorderStyleValues.Thin).Border.SetTopBorderColor(GridLine);
            labelCell.Style.Border.SetLeftBorder(XLBorderStyleValues.Thin).Border.SetLeftBorderColor(GridLine);
            labelCell.Style.Border.SetRightBorder(XLBorderStyleValues.Thin).Border.SetRightBorderColor(GridLine);

            var valueCell = ws.Range(startRow + 1, col, startRow + 1, col + 1).Merge();
            valueCell.FirstCell().Value = value;
            valueCell.Style.NumberFormat.Format = MoneyFormat;
            valueCell.Style.Font.SetFontSize(13).Font.SetBold().Font.SetFontColor(color);
            valueCell.Style.Fill.SetBackgroundColor(CardFill);
            valueCell.Style.Alignment.SetIndent(1);
            valueCell.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin).Border.SetBottomBorderColor(GridLine);
            valueCell.Style.Border.SetLeftBorder(XLBorderStyleValues.Thin).Border.SetLeftBorderColor(GridLine);
            valueCell.Style.Border.SetRightBorder(XLBorderStyleValues.Thin).Border.SetRightBorderColor(GridLine);
        }

        ws.Row(startRow).Height = 16;
        ws.Row(startRow + 1).Height = 22;

        return startRow + 3; // kartlardan sonra bir satır boşluk
    }

    private static void BuildTable(IXLWorksheet ws, List<TxRow> rows, int startRow)
    {
        var headers = new[] { "Tarih", "Açıklama", "Detay", "Tür", "Referans / Not", "Giriş", "Çıkış", "Bakiye" };
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(startRow, FirstColumn + i);
            cell.Value = headers[i];
            cell.Style.Font.SetBold().Font.SetFontColor(HeaderText).Font.SetFontSize(10);
            cell.Style.Fill.SetBackgroundColor(Brand);
            cell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            cell.Style.Border.SetBottomBorder(XLBorderStyleValues.Medium).Border.SetBottomBorderColor(BrandDark);
            cell.Style.Alignment.SetHorizontal(i >= 5
                ? XLAlignmentHorizontalValues.Right
                : XLAlignmentHorizontalValues.Left);
        }
        ws.Row(startRow).Height = 20;

        var dataRow = startRow + 1;
        var zebra = false;
        foreach (var r in rows)
        {
            // Açılış (devir) satırı bir işlem değildir; özet kartlarında toplam giriş/çıkışa
            // dahil edilmediği için Giriş/Çıkış sütunları boş bırakılır, yalnızca Bakiye gösterilir.
            var isOpening = r.Kind == TxKind.Acilis;
            var inflow = !isOpening && r.Amount > 0 ? r.Amount : (decimal?)null;
            var outflow = !isOpening && r.Amount < 0 ? Math.Abs(r.Amount) : (decimal?)null;

            ws.Cell(dataRow, 1).Value = r.Date;
            ws.Cell(dataRow, 1).Style.DateFormat.Format = "dd.MM.yyyy";
            ws.Cell(dataRow, 2).Value = r.Description;
            ws.Cell(dataRow, 3).Value = r.Subline ?? string.Empty;
            ws.Cell(dataRow, 4).Value = KindLabel(r.Kind);
            ws.Cell(dataRow, 5).Value = BuildReference(r);

            if (inflow.HasValue)
            {
                ws.Cell(dataRow, 6).Value = inflow.Value;
                ws.Cell(dataRow, 6).Style.Font.SetFontColor(Inflow).Font.SetBold();
            }
            if (outflow.HasValue)
            {
                ws.Cell(dataRow, 7).Value = outflow.Value;
                ws.Cell(dataRow, 7).Style.Font.SetFontColor(Outflow).Font.SetBold();
            }
            ws.Cell(dataRow, 8).Value = r.RunningBalance;
            ws.Cell(dataRow, 8).Style.Font.SetBold();
            if (r.RunningBalance < 0) ws.Cell(dataRow, 8).Style.Font.SetFontColor(Outflow);

            for (var c = 6; c <= 8; c++)
            {
                ws.Cell(dataRow, c).Style.NumberFormat.Format = MoneyFormat;
                ws.Cell(dataRow, c).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            }
            ws.Cell(dataRow, 3).Style.Font.SetFontColor(Muted);

            var rowRange = ws.Range(dataRow, FirstColumn, dataRow, LastColumn);
            rowRange.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin).Border.SetBottomBorderColor(GridLine);
            rowRange.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            if (r.Kind == TxKind.Acilis)
            {
                rowRange.Style.Font.SetItalic().Font.SetFontColor(Muted);
                rowRange.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FEF9C3")); // amber-100
            }
            else if (zebra)
            {
                rowRange.Style.Fill.SetBackgroundColor(ZebraFill);
            }

            ws.Cell(dataRow, 1).Style.Alignment.SetIndent(1);
            ws.Row(dataRow).Height = 17;
            zebra = !zebra;
            dataRow++;
        }

        if (rows.Count == 0)
        {
            var empty = ws.Range(dataRow, FirstColumn, dataRow, LastColumn).Merge();
            empty.FirstCell().Value = "Seçilen filtreye uygun işlem bulunamadı.";
            empty.Style.Font.SetItalic().Font.SetFontColor(Muted);
            empty.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Row(dataRow).Height = 24;
        }

        // Başlık satırını dondur (üstteki başlık/özet bloğu dahil sabit kalır).
        ws.SheetView.FreezeRows(startRow);
    }

    private static string BuildReference(TxRow r)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.ReferenceNo)) parts.Add(r.ReferenceNo!);
        if (!string.IsNullOrWhiteSpace(r.Note)) parts.Add(r.Note!);
        return string.Join(" · ", parts);
    }

    private static string KindLabel(TxKind kind) => kind switch
    {
        TxKind.Tahsilat => "Tahsilat",
        TxKind.Cikis => "Gider",
        TxKind.Girdi => "Gelir",
        TxKind.Transfer => "Transfer",
        TxKind.Acilis => "Açılış",
        _ => kind.ToString()
    };

    private static string DescribeFilter(CashBankDetailQuery q)
    {
        var typeText = (q.Type ?? "all") switch
        {
            "tahsilat" => "Tahsilat",
            "cikis" => "Para Çıkışı",
            "transfer" => "Transfer",
            _ => "Tüm işlemler"
        };

        var rangeText = (q.Range ?? "all") switch
        {
            "this_month" => "Bu ay",
            "last_month" => "Geçen ay",
            "last_90" => "Son 90 gün",
            "custom" => q.From is not null || q.To is not null
                ? $"{(q.From?.ToString("dd.MM.yyyy") ?? "…")} – {(q.To?.ToString("dd.MM.yyyy") ?? "…")}"
                : "Tarih aralığı",
            _ => "Tüm tarihler"
        };

        var text = $"{typeText}, {rangeText}";
        if (!string.IsNullOrWhiteSpace(q.Q)) text += $", \"{q.Q}\"";
        return text;
    }

    private static string SafeSheetName(string name)
    {
        var invalid = new[] { '[', ']', ':', '*', '?', '/', '\\' };
        var cleaned = new string((name ?? "Hareketler").Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (cleaned.Length > 31) cleaned = cleaned[..31];
        // Excel çalışma sayfası adı tek tırnakla (') başlayamaz veya bitemez; kırpma sonrası
        // sona tırnak gelebileceği için uzunluk sınırından SONRA temizlenir.
        cleaned = cleaned.Trim('\'').Trim();
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "Hareketler";
        return cleaned;
    }
}
