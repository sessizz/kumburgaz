using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Services;

public interface IReportingService
{
    Task<List<DuesDebtReportRow>> GetDuesDebtReportAsync(DuesDebtReportQuery query);
    byte[] ExportDuesDebtAsExcel(List<DuesDebtReportRow> rows, DuesDebtReportQuery? query = null);
    byte[] ExportDuesDebtAsPdf(List<DuesDebtReportRow> rows, DuesDebtReportQuery? query = null);
    Task<AttendanceReportViewModel> GetAttendanceReportAsync(AttendanceReportQuery query);
    byte[] ExportAttendanceAsExcel(AttendanceReportViewModel model);
    byte[] ExportAttendanceAsPdf(AttendanceReportViewModel model);
    DuesStatusReportViewModel BuildDuesStatusReport(List<DuesDebtReportRow> rows, DuesDebtReportQuery query);
    byte[] ExportDuesStatusAsExcel(DuesStatusReportViewModel model);
    byte[] ExportDuesStatusAsPdf(DuesStatusReportViewModel model);
}
