using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

/// <summary>
/// Tarih araligindaki gider kategori dagilimi. Mobil Panel ve Raporlar AYNI kaynagi kullanir.
/// </summary>
public static class CategoryExpenseHelper
{
    public static async Task<List<MobileCategoryAmount>> GetAsync(ApplicationDbContext db, DateTime start, DateTime end)
    {
        return await db.LedgerTransactions
            .Where(x => !x.IsTransfer
                && x.Date >= start && x.Date < end
                && x.IncomeExpenseCategory != null
                && x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gider)
            .GroupBy(x => x.IncomeExpenseCategory!.Name)
            .Select(g => new MobileCategoryAmount { Name = g.Key, Amount = g.Sum(t => t.Amount) })
            .OrderByDescending(x => x.Amount)
            .ToListAsync();
    }
}
