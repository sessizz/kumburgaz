using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public class ImportBatchService(ApplicationDbContext db, IHttpContextAccessor httpContextAccessor)
{
    public async Task<ImportBatch> CreateAsync(
        string type,
        string? sourceAccountKind,
        int? sourceAccountId,
        string? fileName,
        string? fileHash = null)
    {
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var countToday = await db.ImportBatches.CountAsync(x => x.ImportNo.StartsWith($"IMP-{today}-"));
        var batch = new ImportBatch
        {
            ImportNo = $"IMP-{today}-{countToday + 1:0000}",
            Type = type,
            SourceAccountKind = sourceAccountKind,
            SourceAccountId = sourceAccountId,
            FileName = fileName,
            FileHash = fileHash,
            Status = ImportBatchStatus.Draft,
            CreatedBy = httpContextAccessor.HttpContext?.User.Identity?.Name,
            CreatedAt = DateTime.UtcNow
        };

        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync();
        return batch;
    }

    public async Task AddRowAsync(
        ImportBatch batch,
        int lineNo,
        object raw,
        string normalizedKey,
        ImportRowStatus status,
        string? errorMessage = null,
        string? createdEntityName = null,
        int? createdEntityId = null)
    {
        db.ImportBatchRows.Add(new ImportBatchRow
        {
            ImportBatchId = batch.Id,
            LineNo = lineNo,
            RawJson = JsonSerializer.Serialize(raw),
            NormalizedKey = normalizedKey,
            Status = status,
            ErrorMessage = errorMessage,
            CreatedEntityName = createdEntityName,
            CreatedEntityId = createdEntityId
        });

        await db.SaveChangesAsync();
    }

    public Task<bool> HasCommittedDuplicateAsync(string normalizedKey)
    {
        return db.ImportBatchRows
            .AsNoTracking()
            .AnyAsync(x => x.NormalizedKey == normalizedKey
                           && x.ImportBatch != null
                           && x.ImportBatch.Status == ImportBatchStatus.Committed);
    }

    public async Task CommitAsync(ImportBatch batch)
    {
        batch.Status = ImportBatchStatus.Committed;
        batch.CommittedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task FailAsync(ImportBatch batch)
    {
        batch.Status = ImportBatchStatus.Failed;
        await db.SaveChangesAsync();
    }

    public async Task RollbackAsync(int batchId)
    {
        var batch = await db.ImportBatches
            .Include(x => x.Rows)
            .FirstOrDefaultAsync(x => x.Id == batchId);

        if (batch is null || batch.Status != ImportBatchStatus.Committed)
        {
            return;
        }

        foreach (var row in batch.Rows.Where(x => x.CreatedEntityId.HasValue))
        {
            var createdEntityId = row.CreatedEntityId.GetValueOrDefault();
            if (row.CreatedEntityName == nameof(Collection))
            {
                var collection = await db.Collections.FindAsync(createdEntityId);
                if (collection is not null) db.Collections.Remove(collection);
            }
            else if (row.CreatedEntityName == nameof(LedgerTransaction))
            {
                var tx = await db.LedgerTransactions.FindAsync(createdEntityId);
                if (tx is not null) db.LedgerTransactions.Remove(tx);
            }

            row.Status = ImportRowStatus.RolledBack;
        }

        batch.Status = ImportBatchStatus.RolledBack;
        batch.RolledBackAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public static string BuildNormalizedKey(params object?[] parts)
    {
        var normalized = string.Join("|", parts.Select(part => Normalize(part?.ToString() ?? string.Empty)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToUpperInvariant().Replace(" ", string.Empty);
    }
}
