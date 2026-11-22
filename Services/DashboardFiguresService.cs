using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public sealed record OverdueDebtFigures(decimal CarriedDebt, decimal DuesDebt)
{
    public decimal Total => CarriedDebt + DuesDebt;
}

public sealed record MonthFigures(decimal Collections, int CollectionCount, decimal OtherIncome, decimal Expense);

/// <summary>
/// Panel (HomeController.Index) ile MCP site_summary aracının ORTAK rakamları: aynı formül iki yerde yaşamasın.
/// </summary>
public class DashboardFiguresService(ApplicationDbContext db, IDuesLedgerRowService ledgerService)
{
    // Panel'in ay penceresi: UTC takvim ayı (monthStart <= Date < nextMonthStart).
    public static DateTime MonthStartUtc(DateTime utcNow)
        => new(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    public async Task<OverdueDebtFigures> GetOverdueDebtAsync()
        => OverdueDebtOf(await ledgerService.GetInstallmentRowsAsync());

    // Devirden kalan borç + vadesi geçmiş ödenmemiş aidat borcu. Gecikmiş aidat borcu seçili dönemle sınırlı
    // değildir: geçmiş bir dönemden kalan ödenmemiş borç, dönem değiştirilse bile hâlâ gerçek bir borçtur.
    public static OverdueDebtFigures OverdueDebtOf(IEnumerable<DuesListItemViewModel> duesRows)
    {
        var rows = duesRows as IList<DuesListItemViewModel> ?? duesRows.ToList();
        var carried = rows.Where(x => x.IsOpeningBalance && x.RemainingAmount > 0).Sum(x => x.RemainingAmount);
        var dues = rows.Where(x => !x.IsOpeningBalance && !x.IsPaid && x.IsOverdue).Sum(x => x.RemainingAmount);
        return new OverdueDebtFigures(carried, dues);
    }

    public async Task<MonthFigures> GetMonthFiguresAsync(DateTime monthStart)
    {
        var nextMonthStart = monthStart.AddMonths(1);

        var collections = await db.Collections
            .Where(x => x.Date >= monthStart && x.Date < nextMonthStart)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;
        var collectionCount = await db.Collections.CountAsync(x => x.Date >= monthStart && x.Date < nextMonthStart);
        var otherIncome = await db.LedgerTransactions
            .Where(x => x.Date >= monthStart && x.Date < nextMonthStart &&
                !x.IsTransfer &&
                x.IncomeExpenseCategory != null &&
                x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gelir)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;
        var expense = await db.LedgerTransactions
            .Where(x => x.Date >= monthStart && x.Date < nextMonthStart &&
                !x.IsTransfer &&
                x.IncomeExpenseCategory != null &&
                x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gider)
            .SumAsync(x => (decimal?)x.Amount) ?? 0m;

        return new MonthFigures(collections, collectionCount, otherIncome, expense);
    }
}
