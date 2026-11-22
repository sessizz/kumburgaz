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
        var (preview, _, _, _) = await ResolveAsync(request, expectedKind);
        return preview;
    }

    public async Task<int> CreateAsync(LedgerEntryRequest request, LedgerEntryKind? expectedKind = null)
    {
        var (preview, channel, cashBoxId, bankAccountId) = await ResolveAsync(request, expectedKind);

        var entity = new LedgerTransaction
        {
            Date = preview.Date,
            IncomeExpenseCategoryId = preview.CategoryId,
            Amount = request.Amount,
            PaymentChannel = channel,
            CashBoxId = cashBoxId,
            BankAccountId = bankAccountId,
            Description = request.Description
        };
        db.LedgerTransactions.Add(entity);
        await db.SaveChangesAsync();
        return entity.Id;
    }

    private async Task<(LedgerEntryPreview Preview, PaymentChannel Channel, int? CashBoxId, int? BankAccountId)> ResolveAsync(
        LedgerEntryRequest request, LedgerEntryKind? expectedKind)
    {
        if (request.Amount <= 0)
        {
            throw new InvalidOperationException("Tutar sıfırdan büyük olmalı.");
        }

        var category = await db.IncomeExpenseCategories.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.CategoryId);
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

        const string invalidAccount = "Geçerli bir kasa/banka hesabı seçiniz.";
        if (!FinancialAccountHelper.TryParse(request.AccountKey, out var channel, out var cashBoxId, out var bankAccountId))
        {
            throw new InvalidOperationException(invalidAccount);
        }

        string? accountName;
        string accountKey;
        if (cashBoxId.HasValue)
        {
            var id = cashBoxId.Value;
            accountKey = FinancialAccountHelper.CashKey(id);
            accountName = await db.CashBoxes.AsNoTracking()
                .Where(x => x.Id == id && x.Active)
                .Select(x => x.Name).FirstOrDefaultAsync();
        }
        else
        {
            var id = bankAccountId!.Value;
            accountKey = FinancialAccountHelper.BankKey(id);
            accountName = await db.BankAccounts.AsNoTracking()
                .Where(x => x.Id == id && x.Active)
                .Select(x => x.Name).FirstOrDefaultAsync();
        }

        if (accountName is null)
        {
            throw new InvalidOperationException(invalidAccount);
        }

        var preview = new LedgerEntryPreview(kind, category.Id, category.Name, request.Amount,
            DateTimeHelper.EnsureUtc(request.Date), accountKey, accountName, request.Description);
        return (preview, channel, cashBoxId, bankAccountId);
    }
}
