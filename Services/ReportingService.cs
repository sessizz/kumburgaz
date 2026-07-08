using ClosedXML.Excel;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Kumburgaz.Web.Services;

public class ReportingService(ApplicationDbContext db, UnitLedgerService unitLedgerService) : IReportingService
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
                BlockName = x.Block?.Name ?? string.Empty,
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
                BlockName = BuildGroupBlockName(first.BillingGroup),
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
                        UnitDisplay = UnitDisplayHelper.Display(x),
                        UnitNo = x.UnitNo,
                        ResponsibleAccountName = ownerAccount?.Name ?? x.OwnerName ?? string.Empty,
                        Phone = ownerAccount?.Phone ?? x.Phone ?? string.Empty
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
        ws.Cell(1, 1).Value = "Hazirun Cetveli";
        ws.Cell(2, 1).Value = $"Filtre: BlokId {model.Query.BlockId?.ToString() ?? "Tüm"}";
        ws.Range(1, 1, 1, 5).Merge().Style.Font.Bold = true;
        ws.Range(2, 1, 2, 5).Merge();

        var rowIndex = 4;
        foreach (var block in model.Blocks)
        {
            ws.Cell(rowIndex, 1).Value = $"{block.BlockName} Blok";
            ws.Range(rowIndex, 1, rowIndex, 5).Merge().Style.Font.Bold = true;
            rowIndex++;

            ws.Cell(rowIndex, 1).Value = "No";
            ws.Cell(rowIndex, 2).Value = "Daire";
            ws.Cell(rowIndex, 3).Value = "Malik / Sorumlu";
            ws.Cell(rowIndex, 4).Value = "Telefon";
            ws.Cell(rowIndex, 5).Value = "İmza";
            ws.Range(rowIndex, 1, rowIndex, 5).Style.Font.Bold = true;
            rowIndex++;

            var sequence = 1;
            foreach (var row in block.Rows)
            {
                ws.Cell(rowIndex, 1).Value = sequence++;
                ws.Cell(rowIndex, 2).Value = row.UnitDisplay;
                ws.Cell(rowIndex, 3).Value = row.ResponsibleAccountName;
                ws.Cell(rowIndex, 4).Value = row.Phone;
                ws.Cell(rowIndex, 5).Value = string.Empty;
                ws.Row(rowIndex).Height = 28;
                rowIndex++;
            }

            rowIndex++;
        }

        ws.Column(5).Width = 30;
        ws.Columns(1, 4).AdjustToContents();
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
                page.Margin(20);
                page.Header().Column(column =>
                {
                    column.Item().Text("Hazirun Cetveli").FontSize(18).Bold();
                    column.Item().Text($"Filtre: BlokId {model.Query.BlockId?.ToString() ?? "Tüm"}").FontSize(9);
                });
                page.Content().Column(column =>
                {
                    foreach (var block in model.Blocks)
                    {
                        column.Item().PaddingTop(8).Text($"{block.BlockName} Blok").FontSize(13).Bold();
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(28);
                                c.RelativeColumn(2);
                                c.RelativeColumn(3);
                                c.RelativeColumn(2);
                                c.RelativeColumn(3);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("No").Bold();
                                header.Cell().Text("Daire").Bold();
                                header.Cell().Text("Malik / Sorumlu").Bold();
                                header.Cell().Text("Telefon").Bold();
                                header.Cell().Text("İmza").Bold();
                            });

                            var sequence = 1;
                            foreach (var row in block.Rows)
                            {
                                table.Cell().MinHeight(28).Text(sequence++.ToString());
                                table.Cell().MinHeight(28).Text(row.UnitDisplay);
                                table.Cell().MinHeight(28).Text(row.ResponsibleAccountName);
                                table.Cell().MinHeight(28).Text(string.IsNullOrWhiteSpace(row.Phone) ? "-" : row.Phone);
                                table.Cell().MinHeight(28).BorderBottom(0.5f).Text(string.Empty);
                            }
                        });
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

        var blockChunks = model.Blocks.Chunk(3).ToList();
        var rowOffset = 3;
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
                        header.Width(250).Border(1).Background(Colors.Yellow.Medium).PaddingVertical(3).AlignCenter()
                            .Text(DuesStatusTitle(model.Query)).FontSize(12).Bold();
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
        if (string.IsNullOrWhiteSpace(row.BlockName))
        {
            return row.UnitDisplay;
        }

        var prefix = $"{row.BlockName}-";
        return row.UnitDisplay.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? row.UnitDisplay
            : row.UnitDisplay;
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
        return string.IsNullOrWhiteSpace(query.Period)
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
            $"BlokId: {query.BlockId?.ToString() ?? "Tüm"}",
            $"AidatTipiId: {query.DuesTypeId?.ToString() ?? "Tüm"}",
            $"AidatGrubuId: {query.BillingGroupId?.ToString() ?? "Tüm"}",
            $"Bakiye: {query.BalanceStatus ?? "Tümü"}"
        };

        return "Filtre: " + string.Join(" | ", parts);
    }

    private static string UnitKey(int unitId) => $"U:{unitId}";

    private static string GroupKey(int billingGroupId) => $"G:{billingGroupId}";
}
