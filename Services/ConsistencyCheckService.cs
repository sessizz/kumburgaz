using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public class ConsistencyCheckService(ApplicationDbContext db, UnitLedgerService unitLedgerService)
{
    private const decimal Tolerance = 0.01m;

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var previous = await db.ConsistencyCheckResults
            .Where(x => !x.Resolved)
            .ToListAsync(cancellationToken);
        foreach (var item in previous)
        {
            item.Resolved = true;
            item.ResolvedAt = DateTime.UtcNow;
        }

        var issues = new List<ConsistencyCheckResult>();
        await CheckInstallmentAllocationsAsync(issues, cancellationToken);
        await CheckCollectionAllocationsAsync(issues, cancellationToken);
        await CheckUnitLedgerBalancesAsync(issues, cancellationToken);
        await CheckFinancialAccountAssignmentsAsync(issues, cancellationToken);
        await CheckTransferPairsAsync(issues, cancellationToken);
        await CheckImportRowsAsync(issues, cancellationToken);

        db.ConsistencyCheckResults.AddRange(issues);
        await db.SaveChangesAsync(cancellationToken);
        return issues.Count;
    }

    private async Task CheckInstallmentAllocationsAsync(List<ConsistencyCheckResult> issues, CancellationToken cancellationToken)
    {
        var installments = await db.DuesInstallments
            .AsNoTracking()
            .Select(x => new
            {
                x.Id,
                x.Amount,
                x.RemainingAmount,
                x.Status,
                Allocated = x.Allocations.Sum(a => (decimal?)a.AppliedAmount).GetValueOrDefault()
            })
            .ToListAsync(cancellationToken);

        foreach (var installment in installments)
        {
            if (installment.Allocated - installment.Amount > Tolerance)
            {
                AddIssue(
                    issues,
                    "Aidat tahsis fazlası",
                    "Error",
                    $"Aidat #{installment.Id} tahsilat dağılımı tahakkuk tutarını aşıyor.",
                    nameof(DuesInstallment),
                    installment.Id,
                    installment.Allocated - installment.Amount);
            }

            if (installment.RemainingAmount < -Tolerance || installment.RemainingAmount - installment.Amount > Tolerance)
            {
                AddIssue(
                    issues,
                    "Aidat kalan aralığı",
                    "Error",
                    $"Aidat #{installment.Id} kalan tutarı 0 ile tahakkuk tutarı arasında değil.",
                    nameof(DuesInstallment),
                    installment.Id,
                    installment.RemainingAmount);
            }

            var expected = installment.Amount - installment.Allocated;
            var diff = installment.RemainingAmount - expected;
            if (Math.Abs(diff) >= Tolerance)
            {
                AddIssue(
                    issues,
                    "Aidat kalan tutarı",
                    "Error",
                    $"Aidat #{installment.Id} kalan tutarı allocation toplamıyla uyuşmüyor.",
                    nameof(DuesInstallment),
                    installment.Id,
                    diff);
            }

            var expectedStatus = ResolveInstallmentStatus(installment.Amount, installment.RemainingAmount);
            if (installment.Status != expectedStatus)
            {
                AddIssue(
                    issues,
                    "Aidat durum etiketi",
                    "Warning",
                    $"Aidat #{installment.Id} durumu kalan tutarla uyuşmuyor. Beklenen: {expectedStatus}.",
                    nameof(DuesInstallment),
                    installment.Id);
            }
        }
    }

    private async Task CheckCollectionAllocationsAsync(List<ConsistencyCheckResult> issues, CancellationToken cancellationToken)
    {
        var collections = await db.Collections
            .AsNoTracking()
            .Select(x => new
            {
                x.Id,
                x.UnitId,
                x.BillingGroupId,
                x.Amount,
                Allocated = x.Allocations.Sum(a => (decimal?)a.AppliedAmount).GetValueOrDefault()
            })
            .ToListAsync(cancellationToken);

        foreach (var collection in collections)
        {
            var credit = collection.Amount - collection.Allocated;
            if (collection.Allocated - collection.Amount > Tolerance)
            {
                AddIssue(
                    issues,
                    "Tahsilat tahsis fazlası",
                    "Error",
                    $"Tahsilat #{collection.Id} aidatlara kendi tutarından fazla dağıtılmış.",
                    nameof(Collection),
                    collection.Id,
                    collection.Allocated - collection.Amount);
            }

            if (credit <= Tolerance)
            {
                continue;
            }

            var hasEligibleDebt = await db.DuesInstallments
                .AsNoTracking()
                .AnyAsync(x => x.BillingGroupId == collection.BillingGroupId
                               && x.RemainingAmount > Tolerance
                               && (x.UnitId == collection.UnitId || x.UnitId == null),
                    cancellationToken);

            if (hasEligibleDebt)
            {
                AddIssue(
                    issues,
                    "Avans açık borca uygulanmamış",
                    "Warning",
                    $"Tahsilat #{collection.Id} üzerinde {credit:N2} TL avans var ama aynı daire/grupta açık aidat bulunuyor.",
                    nameof(Collection),
                    collection.Id,
                    credit);
            }
        }
    }

    private async Task CheckUnitLedgerBalancesAsync(List<ConsistencyCheckResult> issues, CancellationToken cancellationToken)
    {
        var units = await db.Units
            .AsNoTracking()
            .Include(x => x.Block)
            .Where(x => x.Active)
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .ToListAsync(cancellationToken);

        foreach (var unit in units)
        {
            var ledger = await unitLedgerService.BuildAsync(unit.Id);
            if (ledger is null)
            {
                continue;
            }

            var lastBalance = ledger.Entries.LastOrDefault()?.RunningBalance ?? 0m;
            var runningDiff = ledger.Summary.NetBalance - lastBalance;
            if (Math.Abs(runningDiff) >= Tolerance)
            {
                AddIssue(
                    issues,
                    "Daire ledger toplamı",
                    "Error",
                    $"{UnitDisplayHelper.Display(unit)} ledger özet bakiyesi son satır bakiyesiyle uyuşmuyor.",
                    nameof(Unit),
                    unit.Id,
                    runningDiff);
            }

            var installmentIds = await db.DuesInstallments
                .AsNoTracking()
                .Include(x => x.BillingGroup)
                .ThenInclude(x => x!.Units)
                .Where(x => x.UnitId == unit.Id
                            || (x.UnitId == null && x.BillingGroup!.Units.Any(u => u.UnitId == unit.Id)))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            var duesRemaining = await db.DuesInstallments
                .AsNoTracking()
                .Where(x => installmentIds.Contains(x.Id))
                .SumAsync(x => (decimal?)x.RemainingAmount, cancellationToken) ?? 0m;

            var collectionIdsFromAllocations = await db.CollectionAllocations
                .AsNoTracking()
                .Where(x => installmentIds.Contains(x.DuesInstallmentId))
                .Select(x => x.CollectionId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var unappliedCollectionCredit = await db.Collections
                .AsNoTracking()
                .Where(x => x.UnitId == unit.Id || collectionIdsFromAllocations.Contains(x.Id))
                .Select(x => x.Amount - x.Allocations.Sum(a => (decimal?)a.AppliedAmount).GetValueOrDefault())
                .SumAsync(x => (decimal?)x, cancellationToken) ?? 0m;

            var openingCredit = unit.OpeningBalance > 0m ? unit.OpeningBalance : 0m;
            var openingDebt = unit.OpeningBalance < 0m ? Math.Abs(unit.OpeningBalance) : 0m;
            var expectedNet = openingDebt - openingCredit + duesRemaining - unappliedCollectionCredit;
            var diff = ledger.Summary.NetBalance - expectedNet;

            if (Math.Abs(diff) >= Tolerance)
            {
                AddIssue(
                    issues,
                    "Daire ledger bakiyesi",
                    "Error",
                    $"{UnitDisplayHelper.Display(unit)} net bakiyesi aidat kalanları, devir ve avans toplamıyla uyuşmuyor.",
                    nameof(Unit),
                    unit.Id,
                    diff);
            }
        }
    }

    private async Task CheckFinancialAccountAssignmentsAsync(List<ConsistencyCheckResult> issues, CancellationToken cancellationToken)
    {
        var collectionIssues = await db.Collections
            .AsNoTracking()
            .Where(x => (x.PaymentChannel == PaymentChannel.Bank && (!x.BankAccountId.HasValue || x.CashBoxId.HasValue))
                        || (x.PaymentChannel == PaymentChannel.Cash && (!x.CashBoxId.HasValue || x.BankAccountId.HasValue)))
            .Select(x => new { x.Id, x.PaymentChannel, x.CashBoxId, x.BankAccountId })
            .ToListAsync(cancellationToken);

        foreach (var row in collectionIssues)
        {
            AddIssue(
                issues,
                "Tahsilat hesap bağlantısı",
                "Error",
                $"Tahsilat #{row.Id} ödeme kanalı ile kasa/banka bağlantısı uyuşmuyor.",
                nameof(Collection),
                row.Id);
        }

        var ledgerIssues = await db.LedgerTransactions
            .AsNoTracking()
            .Where(x => (x.PaymentChannel == PaymentChannel.Bank && (!x.BankAccountId.HasValue || x.CashBoxId.HasValue))
                        || (x.PaymentChannel == PaymentChannel.Cash && (!x.CashBoxId.HasValue || x.BankAccountId.HasValue)))
            .Select(x => new { x.Id, x.PaymentChannel, x.CashBoxId, x.BankAccountId })
            .ToListAsync(cancellationToken);

        foreach (var row in ledgerIssues)
        {
            AddIssue(
                issues,
                "Gelir/gider hesap bağlantısı",
                "Error",
                $"Gelir/gider hareketi #{row.Id} ödeme kanalı ile kasa/banka bağlantısı uyuşmuyor.",
                nameof(LedgerTransaction),
                row.Id);
        }
    }

    private async Task CheckTransferPairsAsync(List<ConsistencyCheckResult> issues, CancellationToken cancellationToken)
    {
        var transfers = await db.LedgerTransactions
            .AsNoTracking()
            .Where(x => x.IsTransfer)
            .Select(x => new
            {
                x.Id,
                x.Date,
                x.Amount,
                x.Description,
                x.TransferIsIncoming
            })
            .ToListAsync(cancellationToken);

        foreach (var transfer in transfers.Where(x => x.Amount <= 0m))
        {
            AddIssue(
                issues,
                "Transfer tutarı",
                "Error",
                $"Transfer hareketi #{transfer.Id} pozitif tutarla kaydedilmemiş.",
                nameof(LedgerTransaction),
                transfer.Id,
                transfer.Amount);
        }

        foreach (var group in transfers
                     .GroupBy(x => new { x.Date, x.Amount, x.Description })
                     .Where(x => x.Count(t => t.TransferIsIncoming) != x.Count(t => !t.TransferIsIncoming)))
        {
            AddIssue(
                issues,
                "Transfer karşı hareketi",
                "Error",
                $"Transfer hareketleri eşleşmiyor: {group.Key.Date:dd.MM.yyyy}, {group.Key.Amount:N2} TL, {group.Key.Description}.",
                nameof(LedgerTransaction),
                group.OrderBy(x => x.Id).First().Id);
        }
    }

    private async Task CheckImportRowsAsync(List<ConsistencyCheckResult> issues, CancellationToken cancellationToken)
    {
        var rows = await db.ImportBatchRows
            .AsNoTracking()
            .Include(x => x.ImportBatch)
            .Where(x => x.ImportBatch != null
                        && x.CreatedEntityId.HasValue
                        && x.CreatedEntityName != null)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            var state = await GetCreatedEntityStateAsync(row.CreatedEntityName, row.CreatedEntityId, cancellationToken);
            if (row.ImportBatch!.Status == ImportBatchStatus.Committed)
            {
                if (!state.Exists)
                {
                    AddIssue(
                        issues,
                        "Import kayıt bağlantısı",
                        "Warning",
                        $"{row.ImportBatch.ImportNo} satır {row.LineNo} oluşturduğu kaydı bulamıyor.",
                        nameof(ImportBatchRow),
                        row.Id);
                }
                else if (state.IsDeleted)
                {
                    AddIssue(
                        issues,
                        "Import kayıt durumu",
                        "Warning",
                        $"{row.ImportBatch.ImportNo} satır {row.LineNo} oluşturduğu kayıt silinmiş görünüyor.",
                        nameof(ImportBatchRow),
                        row.Id);
                }
            }
            else if (row.ImportBatch.Status == ImportBatchStatus.RolledBack)
            {
                if (row.Status != ImportRowStatus.RolledBack)
                {
                    AddIssue(
                        issues,
                        "Import geri alma satırı",
                        "Warning",
                        $"{row.ImportBatch.ImportNo} satır {row.LineNo} batch geri alınmış olmasına rağmen RolledBack durumunda değil.",
                        nameof(ImportBatchRow),
                        row.Id);
                }

                if (state.Exists && !state.IsDeleted)
                {
                    AddIssue(
                        issues,
                        "Import geri alma kaydı",
                        "Error",
                        $"{row.ImportBatch.ImportNo} satır {row.LineNo} geri alınmış ama oluşturduğu kayıt hâlâ aktif.",
                        nameof(ImportBatchRow),
                        row.Id);
                }
            }
        }
    }

    private async Task<(bool Exists, bool IsDeleted)> GetCreatedEntityStateAsync(
        string? entityName,
        int? entityId,
        CancellationToken cancellationToken)
    {
        if (!entityId.HasValue)
        {
            return (false, false);
        }

        return entityName switch
        {
            nameof(Collection) => await db.Collections
                .IgnoreQueryFilters()
                .Where(x => x.Id == entityId.Value)
                .Select(x => new ValueTuple<bool, bool>(true, x.IsDeleted))
                .FirstOrDefaultAsync(cancellationToken),
            nameof(LedgerTransaction) => await db.LedgerTransactions
                .IgnoreQueryFilters()
                .Where(x => x.Id == entityId.Value)
                .Select(x => new ValueTuple<bool, bool>(true, x.IsDeleted))
                .FirstOrDefaultAsync(cancellationToken),
            _ => (true, false)
        };
    }

    private static InstallmentStatus ResolveInstallmentStatus(decimal amount, decimal remainingAmount)
    {
        if (remainingAmount <= Tolerance)
        {
            return InstallmentStatus.Paid;
        }

        return remainingAmount < amount - Tolerance
            ? InstallmentStatus.PartiallyPaid
            : InstallmentStatus.Open;
    }

    private static void AddIssue(
        List<ConsistencyCheckResult> issues,
        string checkName,
        string severity,
        string message,
        string entityName,
        int entityId,
        decimal? difference = null)
    {
        issues.Add(new ConsistencyCheckResult
        {
            CheckName = checkName,
            Severity = severity,
            Message = message,
            EntityName = entityName,
            EntityId = entityId.ToString(),
            Difference = difference,
            CreatedAt = DateTime.UtcNow
        });
    }
}
