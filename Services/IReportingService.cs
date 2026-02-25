using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Services;

public interface IReportingService
{
    Task<List<DuesDebtReportRow>> GetDuesDebtReportAsync(DuesDebtReportQuery query);
    byte[] ExportDuesDebtAsExcel(List<DuesDebtReportRow> rows);
    byte[] ExportDuesDebtAsPdf(List<DuesDebtReportRow> rows);
}
