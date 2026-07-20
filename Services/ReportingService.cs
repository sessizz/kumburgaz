using ClosedXML.Excel;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text.RegularExpressions;

namespace Kumburgaz.Web.Services;

public class ReportingService(ApplicationDbContext db, UnitLedgerService unitLedgerService, IDuesLedgerRowService ledgerService) : IReportingService
{
    public const string AllPeriodsValue = "all";

    public async Task<List<DuesDebtReportRow>> GetDuesDebtReportAsync(DuesDebtReportQuery query)
    {
        var selectedPeriod = string.IsNullOrWhiteSpace(query.Period) || query.Period == AllPeriodsValue
            ? AllPeriodsValue
            : (PeriodHelper.IsValid(query.Period) ? query.Period : AllPeriodsValue);
        query.Period = selectedPeriod;

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
                BlockName = x.Block?.Name ?? string.Empty,
                UnitDisplay = CompactUnitDisplay(x),
                ResponsibleAccountName = x.OwnerName ?? string.Empty,
                DuesTypeName = "Tüm",
                BillingGroupName = "Tüm",
                UnitsText = CompactUnitDisplay(x)
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

        var periodFilteredInstallments = selectedPeriod == AllPeriodsValue
            ? installments
            : installments.Where(x => x.Period == selectedPeriod).ToList();

        foreach (var installment in periodFilteredInstallments.Where(x => x.UnitId.HasValue))
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

        foreach (var group in periodFilteredInstallments.Where(x => x.UnitId == null).GroupBy(x => x.BillingGroupId))
        {
            var first = group.First();
            rowsByKey[GroupKey(group.Key)] = new DuesDebtReportRow
            {
                BillingGroupId = group.Key,
                BlockName = BuildGroupBlockName(first.BillingGroup),
                UnitDisplay = CompactUnitDisplay(BillingGroupDisplayHelper.UnitDisplay(first.BillingGroup)),
                ResponsibleAccountName = first.ResponsibleAccount?.Name ?? string.Empty,
                DuesTypeName = first.BillingGroup?.DuesType?.Name ?? "Aidat",
                BillingGroupName = first.BillingGroup?.Name ?? string.Empty,
                Amount = group.Sum(x => x.Amount),
                RemainingAmount = group.Sum(x => x.RemainingAmount),
                UnitsText = CompactUnitDisplay(BillingGroupDisplayHelper.UnitDisplay(first.BillingGroup))
            };
        }

        if (selectedPeriod != AllPeriodsValue)
        {
            // Belirli bir dönem seçiliyse devir bakiyesi (borç/alacak) yine de toplam bakiyeye
            // dahil edilir; devir borcuna fiilen tahsis edilmiş (kalıcı) tutar düşülür.
            var openingDebtRemaining = await ledgerService.GetOpeningDebtRemainingByUnitAsync(units.Select(x => x.Id));
            foreach (var unit in units)
            {
                if (!rowsByKey.TryGetValue(UnitKey(unit.Id), out var row))
                {
                    continue;
                }

                row.OpeningBalance = unit.OpeningBalance;
                if (unit.OpeningBalance < 0m)
                {
                    row.RemainingAmount += openingDebtRemaining.GetValueOrDefault(unit.Id, -unit.OpeningBalance);
                }
                else if (unit.OpeningBalance > 0m)
                {
                    row.RemainingAmount -= unit.OpeningBalance;
                }
            }
        }
        else if (query.BillingGroupId is null && query.DuesTypeId is null)
        {
            var summaries = await unitLedgerService.BuildSummariesAsync(units.Select(x => x.Id));
            foreach (var unit in units)
            {
                if (rowsByKey.TryGetValue(UnitKey(unit.Id), out var row)
                    && summaries.TryGetValue(unit.Id, out var summary))
                {
                    row.Amount = summary.TotalAccrual;
                    row.RemainingAmount = summary.NetBalance;
                    row.OpeningBalance = unit.OpeningBalance;
                    row.UnallocatedCredit = summary.Advance;
                }
            }
        }
        else
        {
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
                    row.UnallocatedCredit = credit.Value;
                }
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

    public byte[] ExportDuesDebtAsExcel(List<DuesDebtReportRow> rows, DuesDebtReportQuery? query = null)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Daire Bakiye");
        ws.Cell(1, 1).Value = "Daire Borç/Alacak Raporu";
        ws.Cell(2, 1).Value = BuildFilterSummary(query);
        ws.Range(1, 1, 1, 7).Merge().Style.Font.Bold = true;
        ws.Range(2, 1, 2, 7).Merge();

        ws.Cell(4, 1).Value = "Daire/Birleşik";
        ws.Cell(4, 2).Value = "Sorumlu Hesap";
        ws.Cell(4, 3).Value = "Aidat Tipi";
        ws.Cell(4, 4).Value = "Aidat Grubu";
        ws.Cell(4, 5).Value = "Tahakkuk Toplamı";
        ws.Cell(4, 6).Value = "Net Bakiye";
        ws.Cell(4, 7).Value = "Durum";

        var rowIndex = 5;
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

    public byte[] ExportDuesDebtAsPdf(List<DuesDebtReportRow> rows, DuesDebtReportQuery? query = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(20);
                page.Header().Column(column =>
                {
                    column.Item().Text("Daire Borç/Alacak Raporu").FontSize(18).Bold();
                    column.Item().Text(BuildFilterSummary(query)).FontSize(9);
                });
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

    public async Task<AttendanceReportViewModel> GetAttendanceReportAsync(AttendanceReportQuery query)
    {
        var units = await db.Units
            .AsNoTracking()
            .Include(x => x.Block)
            .Include(x => x.UnitAccounts.Where(ua => ua.Active && ua.Role == UnitAccountRole.Owner))
            .ThenInclude(x => x.Account)
            .Where(x => x.Active)
            .Where(x => query.BlockId == null || x.BlockId == query.BlockId)
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .ToListAsync();

        var blocks = units
            .GroupBy(x => x.Block?.Name ?? "-")
            .Select(g => new AttendanceReportBlock
            {
                BlockName = g.Key,
                Rows = g.Select(x =>
                {
                    var ownerAccount = x.UnitAccounts
                        .OrderByDescending(ua => ua.StartDate ?? DateTime.MinValue)
                        .Select(ua => ua.Account)
                        .FirstOrDefault(a => a is not null);

                    return new AttendanceReportRow
                    {
                        UnitId = x.Id,
                        UnitDisplay = CompactUnitDisplay(x),
                        UnitNo = x.UnitNo,
                        ResponsibleAccountName = ownerAccount?.Name ?? x.OwnerName ?? string.Empty
                    };
                }).ToList()
            })
            .ToList();

        return new AttendanceReportViewModel
        {
            Query = query,
            Blocks = blocks
        };
    }

    public byte[] ExportAttendanceAsExcel(AttendanceReportViewModel model)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Hazirun");
        ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        ws.PageSetup.Margins.Left = 0.748;
        ws.PageSetup.Margins.Right = 0.748;
        ws.PageSetup.Margins.Top = 0.394;
        ws.PageSetup.Margins.Bottom = 0.394;
        ws.PageSetup.Margins.Header = 0.512;
        ws.PageSetup.Margins.Footer = 0.748;
        ws.Column(1).Width = 3.42578125;
        ws.Column(2).Width = 13.5703125;
        ws.Column(3).Width = 31.28515625;
        ws.Column(4).Width = 38.7109375;

        var rowIndex = 1;
        ws.Cell(rowIndex, 1).Value = "HAZİRUN CETVELİ";
        ws.Range(rowIndex, 1, rowIndex, 4).Merge().Style.Font.SetBold().Font.SetFontSize(14);
        rowIndex++;
        ws.Cell(rowIndex, 1).Value = BuildAttendanceFilterSummary(model.Query);
        ws.Range(rowIndex, 1, rowIndex, 4).Merge().Style.Font.FontSize = 9;
        rowIndex += 2;

        foreach (var block in model.Blocks)
        {
            WriteAttendanceHeader(ws, rowIndex);
            rowIndex++;

            var sequence = 1;
            foreach (var row in block.Rows)
            {
                ws.Cell(rowIndex, 1).Value = sequence++;
                ws.Cell(rowIndex, 2).Value = row.UnitDisplay;
                ws.Cell(rowIndex, 3).Value = row.ResponsibleAccountName;
                ws.Cell(rowIndex, 4).Value = string.Empty;
                ws.Row(rowIndex).Height = 17.45;
                ws.Range(rowIndex, 1, rowIndex, 4).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Range(rowIndex, 1, rowIndex, 4).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                rowIndex++;
            }

            rowIndex++;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] ExportAttendanceAsPdf(AttendanceReportViewModel model)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Portrait());
                page.MarginHorizontal(54);
                page.MarginVertical(28);
                page.Header().Column(header =>
                {
                    header.Item().Text("HAZİRUN CETVELİ").FontSize(13).Bold();
                    header.Item().Text(BuildAttendanceFilterSummary(model.Query)).FontSize(8);
                });
                page.Content().Column(column =>
                {
                    foreach (var block in model.Blocks)
                    {
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(28);
                                c.ConstantColumn(86);
                                c.RelativeColumn(3);
                                c.RelativeColumn(4);
                            });

                            table.Header(header =>
                            {
                                AttendancePdfHeaderCell(header.Cell(), "No");
                                AttendancePdfHeaderCell(header.Cell(), "Daire");
                                AttendancePdfHeaderCell(header.Cell(), "Malik / Sorumlu");
                                AttendancePdfHeaderCell(header.Cell(), "İmza");
                            });

                            var sequence = 1;
                            foreach (var row in block.Rows)
                            {
                                AttendancePdfBodyCell(table.Cell(), sequence++.ToString());
                                AttendancePdfBodyCell(table.Cell(), row.UnitDisplay);
                                AttendancePdfBodyCell(table.Cell(), row.ResponsibleAccountName);
                                AttendancePdfBodyCell(table.Cell(), string.Empty);
                            }
                        });
                        column.Item().Height(8);
                    }
                });
            });
        }).GeneratePdf();
    }

    private static void WriteAttendanceHeader(IXLWorksheet ws, int rowIndex)
    {
        ws.Cell(rowIndex, 1).Value = "No";
        ws.Cell(rowIndex, 2).Value = "Daire";
        ws.Cell(rowIndex, 3).Value = "Malik / Sorumlu";
        ws.Cell(rowIndex, 4).Value = "İmza";
        ws.Range(rowIndex, 1, rowIndex, 4).Style.Font.Bold = true;
    }

    private static void AttendancePdfHeaderCell(IContainer container, string text)
    {
        container
            .PaddingVertical(3)
            .PaddingHorizontal(2)
            .Text(text)
            .FontSize(9)
            .Bold();
    }

    private static void AttendancePdfBodyCell(IContainer container, string text)
    {
        container
            .Border(0.5f)
            .MinHeight(16)
            .PaddingHorizontal(2)
            .AlignMiddle()
            .Text(text)
            .FontSize(9);
    }

    public DuesStatusReportViewModel BuildDuesStatusReport(List<DuesDebtReportRow> rows, DuesDebtReportQuery query)
    {
        return new DuesStatusReportViewModel
        {
            Query = query,
            Blocks = rows
                .GroupBy(x => string.IsNullOrWhiteSpace(x.BlockName) ? "Diğer" : x.BlockName)
                .OrderBy(x => x.Key)
                .Select(g => new DuesStatusReportBlock
                {
                    BlockName = g.Key,
                    Rows = g
                        .OrderBy(x => x.UnitDisplay)
                        .ToList()
                })
                .ToList()
        };
    }

    public byte[] ExportDuesStatusAsExcel(DuesStatusReportViewModel model)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Aidat Durum");
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.PagesWide = 1;
        ws.PageSetup.PagesTall = 2;
        ws.PageSetup.Margins.Left = 0.236;
        ws.PageSetup.Margins.Right = 0.236;
        ws.PageSetup.Margins.Top = 0.394;
        ws.PageSetup.Margins.Bottom = 0.394;
        ws.PageSetup.Margins.Header = 0.315;
        ws.PageSetup.Margins.Footer = 0.315;
        ws.Style.Font.FontSize = 9;

        var reportTitle = DuesStatusTitle(model.Query);
        ws.Cell(1, 4).Value = reportTitle;
        ws.Range(1, 4, 1, 6).Merge();
        ws.Range(1, 4, 1, 6).Style
            .Font.SetBold()
            .Font.SetFontSize(14)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Fill.SetBackgroundColor(XLColor.Yellow)
            .Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        ws.Row(1).Height = 18.75;
        ws.Cell(2, 1).Value = BuildFilterSummary(model.Query);
        ws.Range(2, 1, 2, 11).Merge().Style.Font.FontSize = 8;

        var blockChunks = model.Blocks.Chunk(3).ToList();
        var rowOffset = 4;
        foreach (var blockChunk in blockChunks)
        {
            var maxRows = blockChunk.Max(x => x.Rows.Count);
            ws.Row(rowOffset).Height = 31.5;
            for (var blockIndex = 0; blockIndex < blockChunk.Length; blockIndex++)
            {
                var block = blockChunk[blockIndex];
                var startColumn = blockIndex * 4 + 1;
                ws.Cell(rowOffset, startColumn).Value = "DAİRE NO.";
                ws.Cell(rowOffset, startColumn + 1).Value = "ADI SOYADI";
                ws.Cell(rowOffset, startColumn + 2).Value = "BAKİYE";
                var headerRange = ws.Range(rowOffset, startColumn, rowOffset, startColumn + 2);
                headerRange.Style.Font.Bold = true;
                headerRange.Style.Font.FontSize = 12;
                headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                headerRange.Style.Alignment.WrapText = true;
                headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

                for (var rowIndex = 0; rowIndex < block.Rows.Count; rowIndex++)
                {
                    var row = block.Rows[rowIndex];
                    var excelRow = rowOffset + rowIndex + 1;
                    ws.Row(excelRow).Height = 15.75;
                    ws.Cell(excelRow, startColumn).Value = CompactUnitNo(row);
                    ws.Cell(excelRow, startColumn + 1).Value = row.ResponsibleAccountName;
                    ws.Cell(excelRow, startColumn + 2).Value = CompactBalance(row.RemainingAmount);
                    ws.Range(excelRow, startColumn, excelRow, startColumn + 2).Style.Font.FontSize = 12;
                    ws.Range(excelRow, startColumn, excelRow, startColumn + 2).Style.Border.TopBorder = XLBorderStyleValues.Hair;
                    ws.Range(excelRow, startColumn, excelRow, startColumn + 2).Style.Border.BottomBorder = XLBorderStyleValues.Hair;
                    ws.Cell(excelRow, startColumn).Style.Border.LeftBorder = XLBorderStyleValues.Hair;
                    ws.Cell(excelRow, startColumn + 2).Style.Border.RightBorder = XLBorderStyleValues.Hair;
                    ws.Cell(excelRow, startColumn + 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    if (row.RemainingAmount != 0)
                    {
                        ws.Cell(excelRow, startColumn + 2).Style.Fill.BackgroundColor = XLColor.LightGoldenrodYellow;
                        ws.Cell(excelRow, startColumn + 2).Style.Font.Bold = true;
                    }
                }
            }

            rowOffset += maxRows + 3;
        }

        for (var blockIndex = 0; blockIndex < 3; blockIndex++)
        {
            var startColumn = blockIndex * 4 + 1;
            ws.Column(startColumn).Width = 8.7109375;
            ws.Column(startColumn + 1).Width = 26.7109375;
            ws.Column(startColumn + 2).Width = 8.7109375;
            ws.Column(startColumn + 3).Width = blockIndex == 2 ? 5.7109375 : 4.7109375;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] ExportDuesStatusAsPdf(DuesStatusReportViewModel model)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            const int rowsPerPage = 32;
            var blocks = model.Blocks.Take(3).ToList();
            var maxRows = blocks.Count == 0 ? 0 : blocks.Max(x => x.Rows.Count);
            var pageCount = Math.Max(1, (int)Math.Ceiling(maxRows / (decimal)rowsPerPage));
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.MarginHorizontal(17);
                    page.MarginVertical(28);
                    page.Header().AlignCenter().Element(header =>
                    {
                        header.Column(column =>
                        {
                            column.Item().AlignCenter().Width(250).Border(1).Background(Colors.Yellow.Medium).PaddingVertical(3).AlignCenter()
                                .Text(DuesStatusTitle(model.Query)).FontSize(12).Bold();
                            column.Item().PaddingTop(2).AlignCenter()
                                .Text(BuildFilterSummary(model.Query)).FontSize(7);
                        });
                    });
                    page.Content().PaddingTop(8).Row(row =>
                    {
                        foreach (var block in blocks)
                        {
                            var visibleRows = block.Rows
                                .Skip(pageIndex * rowsPerPage)
                                .Take(rowsPerPage)
                                .ToList();
                            row.RelativeItem().PaddingRight(8).Element(container => BuildCompactDuesStatusBlock(container, visibleRows));
                        }

                        for (var i = blocks.Count; i < 3; i++)
                        {
                            row.RelativeItem();
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
            }
        }).GeneratePdf();
    }

    private static void BuildCompactDuesStatusBlock(IContainer container, List<DuesDebtReportRow> rows)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(1.1f);
                c.RelativeColumn(2.8f);
                c.RelativeColumn(1.1f);
            });

            table.Header(header =>
            {
                CompactHeaderCell(header.Cell(), "DAİRE\nNO.");
                CompactHeaderCell(header.Cell(), "ADI SOYADI");
                CompactHeaderCell(header.Cell(), "BAKİYE");
            });

            foreach (var row in rows)
            {
                CompactBodyCell(table.Cell(), CompactUnitNo(row), alignRight: false, highlight: false);
                CompactBodyCell(table.Cell(), row.ResponsibleAccountName, alignRight: false, highlight: false);
                CompactBodyCell(table.Cell(), CompactBalance(row.RemainingAmount), alignRight: true, highlight: row.RemainingAmount != 0);
            }
        });
    }

    private static void CompactHeaderCell(IContainer container, string text)
    {
        container
            .Border(0.7f)
            .PaddingVertical(4)
            .PaddingHorizontal(2)
            .AlignCenter()
            .AlignMiddle()
            .Text(text)
            .FontSize(6.5f)
            .Bold();
    }

    private static void CompactBodyCell(IContainer container, string text, bool alignRight, bool highlight)
    {
        var cell = container
            .BorderBottom(0.3f)
            .BorderColor(Colors.Grey.Lighten1)
            .MinHeight(10)
            .PaddingHorizontal(2)
            .PaddingVertical(1);

        if (highlight)
        {
            cell = cell.Background(Colors.Yellow.Lighten2);
        }

        var aligned = alignRight ? cell.AlignRight() : cell.AlignLeft();
        var textDescriptor = aligned.Text(string.IsNullOrWhiteSpace(text) ? "-" : text)
            .FontSize(6.2f);
        if (highlight)
        {
            textDescriptor.Bold();
        }
    }

    private static string CompactUnitNo(DuesDebtReportRow row)
    {
        return CompactUnitDisplay(row.UnitDisplay);
    }

    private static string CompactUnitDisplay(Kumburgaz.Web.Models.Unit? unit)
    {
        return CompactUnitDisplay(UnitDisplayHelper.Display(unit));
    }

    private static string CompactUnitDisplay(string display)
    {
        if (string.IsNullOrWhiteSpace(display))
        {
            return "-";
        }

        var compact = display.Split('(', 2)[0].Trim();
        compact = Regex.Replace(compact, @"\s*\+\s*", "+");
        return compact;
    }

    private static string CompactBalance(decimal balance)
    {
        if (balance == 0)
        {
            return "0";
        }

        return decimal.Truncate(balance) == balance
            ? balance.ToString("N0")
            : balance.ToString("N2");
    }

    private static string DuesStatusTitle(DuesDebtReportQuery query)
    {
        return string.IsNullOrWhiteSpace(query.Period) || query.Period == AllPeriodsValue
            ? "AİDAT DURUM GENEL LİSTE"
            : $"{query.Period} DÖNEMİ GENEL LİSTE";
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

    private static string BuildGroupBlockName(BillingGroup? group)
    {
        if (group is null)
        {
            return "Diğer";
        }

        var blockNames = group.Units
            .Select(x => x.Unit?.Block?.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        return blockNames.Count == 0 ? "Diğer" : string.Join(", ", blockNames);
    }

    private static string BuildFilterSummary(DuesDebtReportQuery? query)
    {
        if (query is null)
        {
            return "Filtre: Tümü";
        }

        var parts = new List<string>
        {
            $"Dönem: {(string.IsNullOrWhiteSpace(query.Period) || query.Period == AllPeriodsValue ? "Tümü" : query.Period)}",
            $"BlokId: {query.BlockId?.ToString() ?? "Tüm"}",
            $"AidatTipiId: {query.DuesTypeId?.ToString() ?? "Tüm"}",
            $"AidatGrubuId: {query.BillingGroupId?.ToString() ?? "Tüm"}",
            $"Bakiye: {query.BalanceStatus ?? "Tümü"}"
        };

        return "Filtre: " + string.Join(" | ", parts);
    }

    private static string BuildAttendanceFilterSummary(AttendanceReportQuery? query)
    {
        if (query is null)
        {
            return "Filtre: Tümü";
        }

        return $"Filtre: BlokId: {query.BlockId?.ToString() ?? "Tüm"}";
    }

    private static string UnitKey(int unitId) => $"U:{unitId}";

    private static string GroupKey(int billingGroupId) => $"G:{billingGroupId}";
}
