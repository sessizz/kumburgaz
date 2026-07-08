using ClosedXML.Excel;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
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
        ws.Cell(1, 1).Value = "Aidat Durum Cetveli";
        ws.Cell(2, 1).Value = BuildFilterSummary(model.Query);
        ws.Range(1, 1, 1, 7).Merge().Style.Font.Bold = true;
        ws.Range(2, 1, 2, 7).Merge();

        var rowIndex = 4;
        foreach (var block in model.Blocks)
        {
            ws.Cell(rowIndex, 1).Value = $"{block.BlockName} Blok";
            ws.Range(rowIndex, 1, rowIndex, 7).Merge().Style.Font.Bold = true;
            rowIndex++;

            ws.Cell(rowIndex, 1).Value = "Daire/Birleşik";
            ws.Cell(rowIndex, 2).Value = "Sorumlu Hesap";
            ws.Cell(rowIndex, 3).Value = "Aidat Tipi";
            ws.Cell(rowIndex, 4).Value = "Aidat Grubu";
            ws.Cell(rowIndex, 5).Value = "Tahakkuk";
            ws.Cell(rowIndex, 6).Value = "Bakiye";
            ws.Cell(rowIndex, 7).Value = "Durum";
            ws.Range(rowIndex, 1, rowIndex, 7).Style.Font.Bold = true;
            rowIndex++;

            foreach (var row in block.Rows)
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

            ws.Cell(rowIndex, 4).Value = "Blok Toplamı";
            ws.Cell(rowIndex, 5).Value = block.TotalAccrual;
            ws.Cell(rowIndex, 6).Value = block.TotalDebt - block.TotalCredit;
            ws.Cell(rowIndex, 7).Value = $"Borç {block.TotalDebt:N2} / Alacak {block.TotalCredit:N2}";
            ws.Range(rowIndex, 4, rowIndex, 7).Style.Font.Bold = true;
            rowIndex += 2;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] ExportDuesStatusAsPdf(DuesStatusReportViewModel model)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(18);
                page.Header().Column(column =>
                {
                    column.Item().Text("Aidat Durum Cetveli").FontSize(18).Bold();
                    column.Item().Text(BuildFilterSummary(model.Query)).FontSize(9);
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
                                c.RelativeColumn(2);
                                c.RelativeColumn(3);
                                c.RelativeColumn(2);
                                c.RelativeColumn(2);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Text("Daire").Bold();
                                header.Cell().Text("Sorumlu").Bold();
                                header.Cell().Text("Aidat Tipi").Bold();
                                header.Cell().Text("Grup").Bold();
                                header.Cell().AlignRight().Text("Tahakkuk").Bold();
                                header.Cell().AlignRight().Text("Bakiye").Bold();
                                header.Cell().Text("Durum").Bold();
                            });

                            foreach (var row in block.Rows)
                            {
                                table.Cell().Text(row.UnitDisplay);
                                table.Cell().Text(row.ResponsibleAccountName);
                                table.Cell().Text(row.DuesTypeName);
                                table.Cell().Text(row.BillingGroupName);
                                table.Cell().AlignRight().Text(row.Amount.ToString("N2"));
                                table.Cell().AlignRight().Text(Math.Abs(row.RemainingAmount).ToString("N2"));
                                table.Cell().Text(BalanceStatus(row.RemainingAmount));
                            }
                        });
                        column.Item().AlignRight().Text($"Blok toplamı: Tahakkuk {block.TotalAccrual:N2} TL | Borç {block.TotalDebt:N2} TL | Alacak {block.TotalCredit:N2} TL").FontSize(9).Bold();
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
