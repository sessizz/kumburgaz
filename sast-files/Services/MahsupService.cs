using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

/// <summary>
/// "Mahsuplu gider" akisi: bir daire adina odenen site gideri, aidat borcundan mahsup edilir.
/// Sitenin ana kasasina hem aidat tahsilati (+) hem gider (-) kaydedilir; kasa net etkisi sifirdir.
/// Iki bacak MahsupIslem ile birbirine baglanir ve yalnizca birlikte silinebilir.
/// </summary>
public sealed class MahsupService(
    ApplicationDbContext db,
    ICollectionService collectionService,
    ImageAttachmentService imageAttachmentService)
{
    public sealed class MahsupValidationException(string message) : Exception(message);

    public sealed record MahsupCreateRequest(
        int UnitId,
        int CategoryId,
        decimal Amount,
        string? Description,
        IReadOnlyList<IFormFile> Photos,
        string? CreatedByUserId,
        string? CreatedByUserName);

    public async Task<int> CreateAsync(MahsupCreateRequest request)
    {
        if (request.Amount <= 0)
        {
            throw new MahsupValidationException("Tutar sıfırdan büyük olmalıdır.");
        }

        var unit = await db.Units.Include(x => x.Block).FirstOrDefaultAsync(x => x.Id == request.UnitId)
            ?? throw new MahsupValidationException("Daire bulunamadı.");

        var category = await db.IncomeExpenseCategories
            .FirstOrDefaultAsync(x => x.Id == request.CategoryId && x.Active && x.Type == CategoryTypeHelper.Gider)
            ?? throw new MahsupValidationException("Geçerli bir gider kategorisi seçiniz.");

        var cashBox = await db.CashBoxes.Where(x => x.Active).OrderBy(x => x.Id).FirstOrDefaultAsync()
            ?? throw new MahsupValidationException("Aktif kasa bulunamadı.");

        // Daireye ait tahakkukun billing group'unu cozmek icin bir "anchor" taksit lazim:
        // once acik (kalani > 0) olanlar, yoksa en eski herhangi bir taksit.
        var anchor = await db.DuesInstallments
            .Where(x => x.UnitId == request.UnitId)
            .OrderByDescending(x => x.RemainingAmount > 0)
            .ThenBy(x => x.DueDate)
            .FirstOrDefaultAsync()
            ?? throw new MahsupValidationException("Bu daire için aidat tahakkuk kaydı bulunamadı, mahsup yapılamaz.");

        var unitDisplay = unit.Block is null ? unit.UnitNo : $"{unit.Block.Name}-{unit.UnitNo}";
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var today = DateTimeHelper.EnsureUtc(DateTime.UtcNow.Date);

        var useTransaction = !db.Database.ProviderName!.Contains("InMemory", StringComparison.OrdinalIgnoreCase)
            && db.Database.CurrentTransaction is null;
        await using var tx = useTransaction ? await db.Database.BeginTransactionAsync() : null;

        var collectionId = await collectionService.CreateAsync(new CollectionCreateViewModel
        {
            DuesInstallmentId = anchor.Id,
            Date = today,
            Amount = request.Amount,
            PaymentChannel = PaymentChannel.Cash,
            AccountKey = FinancialAccountHelper.CashKey(cashBox.Id),
            Note = description is null ? $"Mahsup: {category.Name}" : $"Mahsup: {category.Name} - {description}"
        });

        var ledger = new LedgerTransaction
        {
            Date = today,
            IncomeExpenseCategoryId = category.Id,
            Amount = request.Amount,
            PaymentChannel = PaymentChannel.Cash,
            CashBoxId = cashBox.Id,
            Description = description is null ? $"Mahsup - {unitDisplay}" : $"Mahsup - {unitDisplay} - {description}"
        };
        db.LedgerTransactions.Add(ledger);
        await db.SaveChangesAsync();

        foreach (var photo in request.Photos)
        {
            var compressed = await imageAttachmentService.CompressAsync(photo);
            db.Attachments.Add(new Attachment
            {
                EntityType = nameof(LedgerTransaction),
                EntityId = ledger.Id,
                FileName = compressed.FileName,
                ContentType = compressed.ContentType,
                ByteSize = compressed.Content.Length,
                Content = compressed.Content,
                CreatedByUserId = request.CreatedByUserId,
                CreatedByUserName = request.CreatedByUserName
            });
        }

        db.MahsupIslemleri.Add(new MahsupIslem
        {
            CollectionId = collectionId,
            LedgerTransactionId = ledger.Id,
            UnitId = request.UnitId,
            CreatedByUserId = request.CreatedByUserId,
            CreatedByUserName = request.CreatedByUserName
        });

        await db.SaveChangesAsync();

        if (tx is not null)
        {
            await tx.CommitAsync();
        }

        return ledger.Id;
    }

    /// <summary>Mahsubu butun olarak siler: tahsilat allocation'lari geri acilir, gider+ek+baglanti soft delete olur.</summary>
    public async Task<bool> DeleteAsync(int mahsupId)
    {
        var mahsup = await db.MahsupIslemleri.FirstOrDefaultAsync(x => x.Id == mahsupId);
        if (mahsup is null)
        {
            return false;
        }

        var collectionId = mahsup.CollectionId;
        var ledgerTransactionId = mahsup.LedgerTransactionId;

        var useTransaction = !db.Database.ProviderName!.Contains("InMemory", StringComparison.OrdinalIgnoreCase)
            && db.Database.CurrentTransaction is null;
        await using var tx = useTransaction ? await db.Database.BeginTransactionAsync() : null;

        // MahsupIslem once silinip izlemeden (tracking) koparilir; aksi halde EF, Collection/LedgerTransaction
        // silinirken bunlara Restrict FK ile bagli izlenen MahsupIslem'i "iliski koptu" hatasiyla reddeder
        // (soft-delete FK'yi hic sifirlamadigi icin gercek bir kademeli silme yapilamaz).
        db.MahsupIslemleri.Remove(mahsup);
        await db.SaveChangesAsync();
        db.Entry(mahsup).State = EntityState.Detached;

        await collectionService.DeleteAsync(collectionId);

        var ledger = await db.LedgerTransactions.FindAsync(ledgerTransactionId);
        if (ledger is not null)
        {
            db.LedgerTransactions.Remove(ledger);
        }

        var attachments = await db.Attachments
            .Where(x => x.EntityType == nameof(LedgerTransaction) && x.EntityId == ledgerTransactionId)
            .ToListAsync();
        db.Attachments.RemoveRange(attachments);
        await db.SaveChangesAsync();

        if (tx is not null)
        {
            await tx.CommitAsync();
        }

        return true;
    }
}
