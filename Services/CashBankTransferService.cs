using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public sealed record TransferRequest(string FromAccountKey, string ToAccountKey, decimal Amount, DateTime Date, string? Description);
public sealed record TransferPreview(string FromAccountKey, string FromAccountName, string ToAccountKey, string ToAccountName, decimal Amount, DateTime Date, string Description);
public sealed record TransferResult(int OutgoingId, int IncomingId, Guid GroupId);

public class CashBankTransferService(ApplicationDbContext db, TransactionOrderService order)
{
    public async Task<TransferPreview> PreviewAsync(TransferRequest request)
    {
        var (from, to) = await ResolveAccountsAsync(request);
        return new TransferPreview(from.Key, from.Name, to.Key, to.Name, request.Amount,
            DateTime.SpecifyKind(request.Date.Date, DateTimeKind.Utc), FormatDescription(from, to, request.Description));
    }

    public async Task<TransferResult> CreateAsync(TransferRequest request)
    {
        var (from, to) = await ResolveAccountsAsync(request);
        var date = await order.NextSlotAsync(from.Key, request.Date);
        var description = FormatDescription(from, to, request.Description);
        var groupId = Guid.NewGuid();
        var outgoing = new LedgerTransaction { TransferGroupId = groupId };
        var incoming = new LedgerTransaction { TransferGroupId = groupId };

        ApplyLeg(outgoing, from, false, request.Amount, date, description);
        ApplyLeg(incoming, to, true, request.Amount, date, description);
        db.LedgerTransactions.AddRange(outgoing, incoming);
        await db.SaveChangesAsync();
        return new TransferResult(outgoing.Id, incoming.Id, groupId);
    }

    public async Task UpdateAsync(int legId, TransferRequest request)
    {
        var leg = await db.LedgerTransactions.Include(x => x.IncomeExpenseCategory)
            .FirstOrDefaultAsync(x => x.Id == legId);
        var pair = leg is null ? null : await TransferPairing.FindPairAsync(db, leg);
        if (leg is null || pair is null)
        {
            throw new InvalidOperationException("Transferin karşı satırı bulunamadı.");
        }

        var (from, to) = await ResolveAccountsAsync(request);
        var editingIncoming = TransferPairing.IsIncoming(leg);
        var editedAccountKey = editingIncoming ? to.Key : from.Key;
        var date = await order.ResolveEditedTransactionDateAsync(editedAccountKey, request.Date, leg.Date);
        var description = FormatDescription(from, to, request.Description);

        ApplyLeg(editingIncoming ? pair : leg, from, false, request.Amount, date, description);
        ApplyLeg(editingIncoming ? leg : pair, to, true, request.Amount, date, description);
        await db.SaveChangesAsync();
    }

    private async Task<(ActiveFinancialAccount From, ActiveFinancialAccount To)> ResolveAccountsAsync(TransferRequest request)
    {
        if (request.Amount <= 0)
        {
            throw new InvalidOperationException("Tutar sıfırdan büyük olmalı.");
        }

        var from = await FinancialAccountHelper.FindActiveAsync(db, request.FromAccountKey);
        var to = await FinancialAccountHelper.FindActiveAsync(db, request.ToAccountKey);
        if (from is null || to is null)
        {
            throw new InvalidOperationException("Geçerli bir kasa/banka hesabı seçiniz.");
        }

        if (from.Key == to.Key)
        {
            throw new InvalidOperationException("Kaynak ve hedef hesap aynı olamaz.");
        }

        return (from, to);
    }

    private static string FormatDescription(ActiveFinancialAccount from, ActiveFinancialAccount to, string? description)
    {
        var prefix = from.Channel == PaymentChannel.Cash && to.Channel == PaymentChannel.Bank
            ? "Bankaya yatır"
            : "Para transferi";
        return string.IsNullOrWhiteSpace(description) ? prefix : $"{prefix}: {description.TrimEnd()}";
    }

    private static void ApplyLeg(
        LedgerTransaction leg, ActiveFinancialAccount account, bool incoming, decimal amount, DateTime date, string description)
    {
        leg.Date = DateTimeHelper.EnsureUtc(date);
        leg.IncomeExpenseCategoryId = null;
        leg.IsTransfer = true;
        leg.TransferIsIncoming = incoming;
        leg.Amount = amount;
        leg.PaymentChannel = account.Channel;
        leg.CashBoxId = account.CashBoxId;
        leg.BankAccountId = account.BankAccountId;
        leg.Description = description;
    }
}
