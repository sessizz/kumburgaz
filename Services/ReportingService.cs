using ClosedXML.Excel;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Kumburgaz.Web.Services;

public class ReportingService(ApplicationDbContext db) : IReportingService
{
    public async Task<List<DuesDebtReportRow>> GetDuesDebtReportAsync(DuesDebtReportQuery query)
    {
        var rows = await db.DuesInstallments
            .AsNoTracking()
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.DuesType)
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.Units)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Where(x => query.Period == null || x.Period == query.Period)
            .Where(x => query.BillingGroupId == null || x.BillingGroupId == query.BillingGroupId)
            .Where(x => query.DuesTypeId == null || x.BillingGroup!.DuesTypeId == query.DuesTypeId)
            .Where(x => query.BlockId == null || x.BillingGroup!.Units.Any(u => u.Unit!.BlockId == query.BlockId))
            .OrderBy(x => x.Period)
            .ThenBy(x => x.BillingGroup!.Name)
            .Select(x => new DuesDebtReportRow
            {
                BillingGroupId = x.BillingGroupId,
                BillingGroupName = x.BillingGroup!.Name,
                DuesTypeName = x.BillingGroup.DuesType!.Name,
                Period = x.Period,
                Amount = x.Amount,
                RemainingAmount = x.RemainingAmount,
                UnitsText = string.Join(", ", x.BillingGroup.Units
                    .Select(u => $"{u.Unit!.Block!.Name}-{u.Unit.UnitNo}")
                    .OrderBy(v => v))
            })
            .ToListAsync();

        return rows;
    }

    public byte[] ExportDuesDebtAsExcel(List<DuesDebtReportRow> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Aidat Borc");
        ws.Cell(1, 1).Value = "Donem";
        ws.Cell(1, 2).Value = "Grup";
        ws.Cell(1, 3).Value = "Aidat Tipi";
        ws.Cell(1, 4).Value = "Daireler";
        ws.Cell(1, 5).Value = "Tutar";
        ws.Cell(1, 6).Value = "Kalan";

        var rowIndex = 2;
        foreach (var row in rows)
        {
            ws.Cell(rowIndex, 1).Value = row.Period;
            ws.Cell(rowIndex, 2).Value = row.BillingGroupName;
            ws.Cell(rowIndex, 3).Value = row.DuesTypeName;
            ws.Cell(rowIndex, 4).Value = row.UnitsText;
            ws.Cell(rowIndex, 5).Value = row.Amount;
            ws.Cell(rowIndex, 6).Value = row.RemainingAmount;
            rowIndex++;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] ExportDuesDebtAsPdf(List<DuesDebtReportRow> rows)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(20);
                page.Header().Text("Aidat Borc Raporu").FontSize(18).Bold();
                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(1);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                        c.RelativeColumn(3);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Text("Donem");
                        header.Cell().Text("Grup");
                        header.Cell().Text("Tip");
                        header.Cell().Text("Daireler");
                        header.Cell().Text("Tutar");
                        header.Cell().Text("Kalan");
                    });

                    foreach (var row in rows)
                    {
                        table.Cell().Text(row.Period);
                        table.Cell().Text(row.BillingGroupName);
                        table.Cell().Text(row.DuesTypeName);
                        table.Cell().Text(row.UnitsText);
                        table.Cell().Text(row.Amount.ToString("N2"));
                        table.Cell().Text(row.RemainingAmount.ToString("N2"));
                    }
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
}
