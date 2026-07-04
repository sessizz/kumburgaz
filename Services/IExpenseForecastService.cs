using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Services;

public interface IExpenseForecastService
{
    Task<ExpenseForecastResult> BuildAsync(DateTime monthStartUtc);
}

public class ExpenseForecastResult
{
    public List<ExpenseForecastItem> Items { get; set; } = [];
    public decimal Total { get; set; }
    public int Confidence { get; set; }
}
