using ClosedXML.Excel;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Kumburgaz.Web.Services;

public class ReportingService(ApplicationDbContext db) : IReportingService
{
    public async Task<List<DuesDebtReportRow>> GetDuesDebtReportAsync(DuesDebtReportQuery query)
    {
        query.Period = null;

        var units = await db.Units
            .AsNoTracking()
            .Include(x => x.Block)
            .Include(x => x.CombinedUnitMembers)
            .ThenInclude(x => x.ComponentUnit)
            .ThenInclude(x => x!.Block)
            .Where(x => x.Active)
            .Where(x => query.BlockId == null || x.BlockId == query.BlockId)
            .ToListAsync();

        var rowsByKey = units.ToDictionary(
            x => UnitKey(x.Id),
            x => new DuesDebtReportRow
            {
                UnitId = x.Id,
                UnitDisplay = UnitDisplayHelper.Display(x),
                ResponsibleAccountName = x.OwnerName ?? string.Empty,
                DuesTypeName = "Tüm",
                BillingGroupName = "Tüm",
                UnitsText = UnitDisplayHelper.Display(x)
            });

        var installments = await db.DuesInstallments
            .AsNoTracking()
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.DuesType)
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
            .Where(x => query.BillingGroupId == null || x.BillingGroupId == query.BillingGroupId)
            .Where(x => query.DuesTypeId == null || x.BillingGroup!.DuesTypeId == query.DuesTypeId)
            .Where(x => query.BlockId == null || (x.UnitId != null && x.Unit!.BlockId == query.BlockId))
            .OrderBy(x => x.Period)
            .ThenBy(x => x.DueDate)
            .ThenBy(x => x.Id)
            .ToListAsync();

        foreach (var installment in installments.Where(x => x.UnitId.HasValue))
        {
            var key = UnitKey(installment.UnitId!.Value);
            if (!rowsByKey.TryGetValue(key, out var row))
            {
                continue;
            }

            row.Amount += installment.Amount;
            row.RemainingAmount += installment.RemainingAmount;
            row.ResponsibleAccountName = string.IsNullOrWhiteSpace(installment.ResponsibleAccount?.Name)
                ? row.ResponsibleAccountName
                : installment.ResponsibleAccount.Name;
            row.DuesTypeName = AddDistinctName(row.DuesTypeName, installment.BillingGroup?.DuesType?.Name);
            row.BillingGroupName = AddDistinctName(row.BillingGroupName, installment.BillingGroup?.Name);
        }

        foreach (var group in installments.Where(x => x.UnitId == null).GroupBy(x => x.BillingGroupId))
        {
            var first = group.First();
            rowsByKey[GroupKey(group.Key)] = new DuesDebtReportRow
            {
                BillingGroupId = group.Key,
                UnitDisplay = BillingGroupDisplayHelper.UnitDisplay(first.BillingGroup),
                ResponsibleAccountName = first.ResponsibleAccount?.Name ?? string.Empty,
                DuesTypeName = first.BillingGroup?.DuesType?.Name ?? "Aidat",
                BillingGroupName = first.BillingGroup?.Name ?? string.Empty,
                Amount = group.Sum(x => x.Amount),
                RemainingAmount = group.Sum(x => x.RemainingAmount),
                UnitsText = BillingGroupDisplayHelper.UnitDisplay(first.BillingGroup)
            };
        }

        if (query.BillingGroupId is null && query.DuesTypeId is null)
        {
            foreach (var unit in units)
            {
                if (rowsByKey.TryGetValue(UnitKey(unit.Id), out var row))
                {
                    row.RemainingAmount -= unit.OpeningBalance;
                }
            }
        }

        var collectionCredits = await CollectionCreditHelper.BuildUnitCreditMapAsync(
            db,
            query.BlockId,
            query.BillingGroupId,
            query.DuesTypeId);

        foreach (var credit in collectionCredits)
        {
            if (rowsByKey.TryGetValue(UnitKey(credit.Key), out var row))
            {
                row.RemainingAmount -= credit.Value;
            }
        }

        var rows = rowsByKey.Values.AsEnumerable();
        rows = query.BalanceStatus?.Trim().ToLowerInvariant() switch
        {
            "debt" => rows.Where(x => x.RemainingAmount > 0),
            "credit" => rows.Where(x => x.RemainingAmount < 0),
            "clear" => rows.Where(x => x.RemainingAmount == 0),
            _ => rows
        };

        return rows
            .OrderBy(x => x.UnitDisplay)
            .ToList();
    }

    public byte[] ExportDuesDebtAsExcel(List<DuesDebtReportRow> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Daire Bakiye");
        ws.Cell(1, 1).Value = "Daire/Birleşik";
        ws.Cell(1, 2).Value = "Sorumlu Hesap";
        ws.Cell(1, 3).Value = "Aidat Tipi";
        ws.Cell(1, 4).Value = "Aidat Grubu";
        ws.Cell(1, 5).Value = "Tahakkuk Toplamı";
        ws.Cell(1, 6).Value = "Net Bakiye";
        ws.Cell(1, 7).Value = "Durum";

        var rowIndex = 2;
        foreach (var row in rows)
        {
            ws.Cell(rowIndex, 1).Value = row.UnitDisplay;
            ws.Cell(rowIndex, 2).Value = row.ResponsibleAccountName;
            ws.Cell(rowIndex, 3).Value = row.DuesTypeName;
            ws.Cell(rowIndex, 4).Value = row.BillingGroupName;
            ws.Cell(rowIndex, 5).Value = row.Amount;
            ws.Cell(rowIndex, 6).Value = Math.Abs(row.RemainingAmount);
            ws.Cell(rowIndex, 7).Value = BalanceStatus(row.RemainingAmount);
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
                page.Header().Text("Daire Borç/Alacak Raporu").FontSize(18).Bold();
                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(2);
                        c.RelativeColumn(3);
                        c.RelativeColumn(2);
                        c.RelativeColumn(3);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Text("Daire/Birleşik");
                        header.Cell().Text("Sorumlu");
                        header.Cell().Text("Aidat Tipi");
                        header.Cell().Text("Aidat Grubu");
                        header.Cell().Text("Bakiye");
                        header.Cell().Text("Durum");
                    });

                    foreach (var row in rows)
                    {
                        table.Cell().Text(row.UnitDisplay);
                        table.Cell().Text(row.ResponsibleAccountName);
                        table.Cell().Text(row.DuesTypeName);
                        table.Cell().Text(row.BillingGroupName);
                        table.Cell().Text(Math.Abs(row.RemainingAmount).ToString("N2"));
                        table.Cell().Text(BalanceStatus(row.RemainingAmount));
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

    private static string AddDistinctName(string current, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.IsNullOrWhiteSpace(current) ? "Tüm" : current;
        }

        if (string.IsNullOrWhiteSpace(current) || current == "Tüm")
        {
            return name;
        }

        var parts = current.Split(", ", StringSplitOptions.RemoveEmptyEntries);
        return parts.Contains(name, StringComparer.OrdinalIgnoreCase)
            ? current
            : $"{current}, {name}";
    }

    private static string BalanceStatus(decimal balance)
    {
        return balance > 0 ? "Borç" : balance < 0 ? "Alacak" : "Yok";
    }

    private static string UnitKey(int unitId) => $"U:{unitId}";

    private static string GroupKey(int billingGroupId) => $"G:{billingGroupId}";
}
