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
        var installments = await db.DuesInstallments
            .AsNoTracking()
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.DuesType)
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.Units)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Where(x => query.Period == null || x.Period == query.Period)
            .Where(x => query.BillingGroupId == null || x.BillingGroupId == query.BillingGroupId)
            .Where(x => query.DuesTypeId == null || x.BillingGroup!.DuesTypeId == query.DuesTypeId)
            .Where(x => query.BlockId == null || x.BillingGroup!.Units.Any(u => u.Unit!.BlockId == query.BlockId))
            .OrderBy(x => x.Period)
            .ThenBy(x => x.BillingGroup!.Name)
            .ToListAsync();

        var rows = installments
            .Select(x => new DuesDebtReportRow
            {
                InstallmentId = x.Id,
                BillingGroupId = x.BillingGroupId,
                UnitDisplay = x.UnitId.HasValue
                    ? $"{x.Unit!.Block!.Name}-{x.Unit.UnitNo}"
                    : BillingGroupDisplayHelper.UnitDisplay(x.BillingGroup),
                BillingGroupName = x.BillingGroup!.Name,
                DuesTypeName = x.BillingGroup.DuesType!.Name,
                Period = x.Period,
                Amount = x.Amount,
                RemainingAmount = x.RemainingAmount,
                UnitsText = string.Join(", ", x.BillingGroup.Units
                    .Select(u => $"{u.Unit!.Block!.Name}-{u.Unit.UnitNo}")
                    .OrderBy(v => v))
            })
            .OrderBy(x => x.UnitDisplay)
            .ThenBy(x => x.Period)
            .ToList();

        var billingGroupIds = rows.Select(x => x.BillingGroupId).Distinct().ToList();
        if (billingGroupIds.Count > 0)
        {
            var creditByGroup = await db.Collections
                .AsNoTracking()
                .Where(x => billingGroupIds.Contains(x.BillingGroupId))
                .Select(x => new
                {
                    x.BillingGroupId,
                    Credit = x.Amount - x.Allocations.Sum(a => (decimal?)a.AppliedAmount).GetValueOrDefault()
                })
                .GroupBy(x => x.BillingGroupId)
                .Select(x => new { BillingGroupId = x.Key, Credit = x.Sum(v => v.Credit) })
                .ToDictionaryAsync(x => x.BillingGroupId, x => x.Credit);

            foreach (var groupedRows in rows.GroupBy(x => x.BillingGroupId))
            {
                if (!creditByGroup.TryGetValue(groupedRows.Key, out var credit) || credit <= 0)
                {
                    continue;
                }

                var orderedRows = groupedRows
                    .OrderBy(x => x.Period)
                    .ThenBy(x => x.Amount)
                    .ToList();

                foreach (var row in orderedRows)
                {
                    if (credit <= 0)
                    {
                        break;
                    }

                    var reduction = Math.Min(row.RemainingAmount, credit);
                    row.RemainingAmount -= reduction;
                    credit -= reduction;
                }

                if (credit > 0)
                {
                    var anchor = orderedRows.Last();
                    rows.Add(new DuesDebtReportRow
                    {
                        InstallmentId = null,
                        BillingGroupId = anchor.BillingGroupId,
                        UnitDisplay = anchor.UnitDisplay,
                        BillingGroupName = anchor.BillingGroupName,
                        DuesTypeName = "Fazla Ödeme",
                        Period = anchor.Period,
                        Amount = 0,
                        RemainingAmount = -credit,
                        UnitsText = anchor.UnitsText
                    });
                }
            }
        }

        return rows
            .OrderBy(x => x.UnitDisplay)
            .ThenBy(x => x.Period)
            .ToList();
    }

    public byte[] ExportDuesDebtAsExcel(List<DuesDebtReportRow> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Aidat Borç");
        ws.Cell(1, 1).Value = "Dönem";
        ws.Cell(1, 2).Value = "Daire/Birleşik";
        ws.Cell(1, 3).Value = "Aidat Tipi";
        ws.Cell(1, 4).Value = "Aidat Grubu";
        ws.Cell(1, 5).Value = "Tutar";
        ws.Cell(1, 6).Value = "Kalan";

        var rowIndex = 2;
        foreach (var row in rows)
        {
            ws.Cell(rowIndex, 1).Value = row.Period;
            ws.Cell(rowIndex, 2).Value = row.UnitDisplay;
            ws.Cell(rowIndex, 3).Value = row.DuesTypeName;
            ws.Cell(rowIndex, 4).Value = row.BillingGroupName;
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
                page.Header().Text("Aidat Borç Raporu").FontSize(18).Bold();
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
                        header.Cell().Text("Dönem");
                        header.Cell().Text("Daire/Birleşik");
                        header.Cell().Text("Tip");
                        header.Cell().Text("Aidat Grubu");
                        header.Cell().Text("Tutar");
                        header.Cell().Text("Kalan");
                    });

                    foreach (var row in rows)
                    {
                        table.Cell().Text(row.Period);
                        table.Cell().Text(row.UnitDisplay);
                        table.Cell().Text(row.DuesTypeName);
                        table.Cell().Text(row.BillingGroupName);
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
