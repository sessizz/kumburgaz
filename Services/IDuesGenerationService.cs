using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Services;

public interface IDuesGenerationService
{
    Task<List<DuesGenerationPreviewItem>> PreviewAsync(string period);
    Task GenerateForPeriodAsync(string period, DateTime accrualDate, DateTime dueDate, DuesPayerType payerType);
    Task DeleteForPeriodAsync(string period);
}
