using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class ReportingServiceExportTests
{
    [Fact]
    public void DuesStatus_compact_exports_generate_files()
    {
        var service = new ReportingService(null!, null!, null!);
        var model = new DuesStatusReportViewModel
        {
            Query = new DuesDebtReportQuery(),
            Blocks =
            [
                BuildBlock("A", 25),
                BuildBlock("B", 25),
                BuildBlock("C", 25)
            ]
        };

        var excel = service.ExportDuesStatusAsExcel(model);
        var pdf = service.ExportDuesStatusAsPdf(model);

        Assert.True(excel.Length > 1000);
        Assert.True(pdf.Length > 1000);
    }

    private static DuesStatusReportBlock BuildBlock(string blockName, int count)
    {
        var block = new DuesStatusReportBlock { BlockName = blockName };
        for (var i = 1; i <= count; i++)
        {
            block.Rows.Add(new DuesDebtReportRow
            {
                BlockName = blockName,
                UnitDisplay = $"{blockName}-{i}",
                ResponsibleAccountName = $"MALIK {blockName}{i}",
                RemainingAmount = i % 10 == 0 ? -12000 : i % 7 == 0 ? 500 : 0
            });
        }

        return block;
    }
}
