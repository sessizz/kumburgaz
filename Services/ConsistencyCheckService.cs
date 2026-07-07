using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public class ConsistencyCheckService(ApplicationDbContext db)
{
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
                Allocated = x.Allocations.Sum(a => (decimal?)a.AppliedAmount).GetValueOrDefault()
            })
            .ToListAsync(cancellationToken);

        foreach (var installment in installments)
        {
            var expected = installment.Amount - installment.Allocated;
            var diff = installment.RemainingAmount - expected;
            if (Math.Abs(diff) < 0.01m)
            {
                continue;
            }

            issues.Add(new ConsistencyCheckResult
            {
                CheckName = "Aidat kalan tutarı",
                Severity = "Error",
                Message = $"Aidat #{installment.Id} kalan tutarı allocation toplamıyla uyuşmuyor.",
                EntityName = nameof(DuesInstallment),
                EntityId = installment.Id.ToString(),
                Difference = diff
            });
        }
    }

    private async Task CheckImportRowsAsync(List<ConsistencyCheckResult> issues, CancellationToken cancellationToken)
    {
        var rows = await db.ImportBatchRows
            .AsNoTracking()
            .Include(x => x.ImportBatch)
            .Where(x => x.ImportBatch != null
                        && x.ImportBatch.Status == ImportBatchStatus.Committed
                        && x.CreatedEntityId.HasValue
                        && x.CreatedEntityName != null)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            var exists = row.CreatedEntityName switch
            {
                nameof(Collection) => await db.Collections.IgnoreQueryFilters().AnyAsync(x => x.Id == row.CreatedEntityId, cancellationToken),
                nameof(LedgerTransaction) => await db.LedgerTransactions.IgnoreQueryFilters().AnyAsync(x => x.Id == row.CreatedEntityId, cancellationToken),
                _ => true
            };

            if (!exists)
            {
                issues.Add(new ConsistencyCheckResult
                {
                    CheckName = "Import kayıt bağlantısı",
                    Severity = "Warning",
                    Message = $"{row.ImportBatch!.ImportNo} satır {row.LineNo} oluşturduğu kaydı bulamıyor.",
                    EntityName = nameof(ImportBatchRow),
                    EntityId = row.Id.ToString()
                });
            }
        }
    }
}
