using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public enum LedgerEntryKind { Income, Expense }

public sealed record LedgerEntryRequest(DateTime Date, int CategoryId, decimal Amount, string? AccountKey, string? Description);

public sealed record LedgerEntryPreview(
    LedgerEntryKind Kind,
    int CategoryId,
    string CategoryName,
    decimal Amount,
    DateTime Date,
    string AccountKey,
    string AccountName,
    string? Description);

/// <summary>
/// Düz gelir/gider kaydını doğrulayıp oluşturur. Hatalarda Türkçe mesajlı InvalidOperationException atar.
/// </summary>
public class LedgerEntryService(ApplicationDbContext db)
{
    public async Task<LedgerEntryPreview> PreviewAsync(LedgerEntryRequest request, LedgerEntryKind? expectedKind = null)
    {
        var (preview, _) = await ResolveAsync(request, expectedKind);
        return preview;
    }

    public async Task<int> CreateAsync(LedgerEntryRequest request, LedgerEntryKind? expectedKind = null)
    {
        var (preview, account) = await ResolveAsync(request, expectedKind);

        var entity = new LedgerTransaction
        {
            Date = preview.Date,
            IncomeExpenseCategoryId = preview.CategoryId,
            Amount = request.Amount,
            PaymentChannel = account.Channel,
            CashBoxId = account.CashBoxId,
            BankAccountId = account.BankAccountId,
            Description = request.Description
        };
        db.LedgerTransactions.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    private async Task<(LedgerEntryPreview Preview, ActiveFinancialAccount Account)> ResolveAsync(
        LedgerEntryRequest request, LedgerEntryKind? expectedKind)
    {
        if (request.Amount <= 0)
        {
            throw new InvalidOperationException("Tutar sıfırdan büyük olmalı.");
        }

        var category = await db.IncomeExpenseCategories.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.CategoryId && x.Active);
        if (category is null)
        {
            throw new InvalidOperationException("Kategori bulunamadı.");
        }

        var kind = CategoryTypeHelper.Normalize(category.Type) == CategoryTypeHelper.Gelir
            ? LedgerEntryKind.Income
            : LedgerEntryKind.Expense;
        if (expectedKind.HasValue && expectedKind.Value != kind)
        {
            throw new InvalidOperationException(expectedKind.Value == LedgerEntryKind.Expense
                ? "Seçilen kategori bir gider kategorisi değil."
                : "Seçilen kategori bir gelir kategorisi değil.");
        }

        var account = await FinancialAccountHelper.FindActiveAsync(db, request.AccountKey)
            ?? throw new InvalidOperationException("Geçerli bir kasa/banka hesabı seçiniz.");

        var preview = new LedgerEntryPreview(kind, category.Id, category.Name, request.Amount,
            DateTimeHelper.EnsureUtc(request.Date), account.Key, account.Name, request.Description);
        return (preview, account);
    }
}
