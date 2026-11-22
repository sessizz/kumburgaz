using System.Text.Json;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;

namespace Kumburgaz.Web.Mcp;

/// <summary>
/// Yazma araçlarının tekrar koruması: aynı kullanıcı + araç + anahtar 24 saat içinde yeniden gelirse yeni kayıt
/// açılmaz, ilk sonuç döner. Anahtar satırı kayıtla aynı işlemde (transaction) ve kayıttan önce yazılır; eşzamanlı
/// iki çağrıdan biri birincil anahtarda çakışır ya da bekler, bu yüzden tek kayıt oluşur.
/// </summary>
public class McpIdempotencyStore(ApplicationDbContext db)
{
    private const int MinKeyLength = 8;
    private const int MaxKeyLength = 100;

    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public static string RequireKey(string? key)
    {
        var trimmed = key?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new McpException("confirm: true için idempotency_key gönderin.");
        }

        return trimmed.Length is >= MinKeyLength and <= MaxKeyLength
            ? trimmed
            : throw new McpException("idempotency_key 8-100 karakter olmalı.");
    }

    // 24 saatten eskiyse null.
    public async Task<string?> TryGetAsync(string userId, string tool, string key)
    {
        var cutoff = DateTime.UtcNow - Retention;
        return await db.McpIdempotencyKeys.AsNoTracking()
            .Where(x => x.UserId == userId && x.Tool == tool && x.Key == key && x.CreatedAt >= cutoff)
            .Select(x => x.ResultJson)
            .FirstOrDefaultAsync();
    }

    // Yalnız ekler: anahtar zaten varsa DbUpdateException. Önce 24 saatten eski satırları siler.
    public async Task SaveAsync(string userId, string tool, string key, string resultJson)
    {
        await PurgeExpiredAsync();
        db.McpIdempotencyKeys.Add(new McpIdempotencyKey
        {
            UserId = userId,
            Tool = tool,
            Key = key,
            ResultJson = resultJson,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    // Anahtarın saklı sonucu; yoksa null. Anahtar başka girdilerle kullanılmışsa hata: eski sonucu dönmek,
    // hiç yazılmamış bir işlemi yazılmış gösterirdi.
    public async Task<TResult?> TryReplayAsync<TResult>(string userId, string tool, string key, string fingerprint)
        where TResult : class
    {
        var json = await TryGetAsync(userId, tool, key);
        if (json is null)
        {
            return null;
        }

        var stored = JsonSerializer.Deserialize<StoredResult<TResult>>(json, JsonSerializerOptions.Web);
        return stored?.Fingerprint == fingerprint
            ? stored.Result
            : throw new McpException(
                "Bu idempotency_key farklı bir işlem için kullanılmış; yeni işlem için yeni bir anahtar gönderin.");
    }

    /// <summary>
    /// write'ı anahtar başına bir kez çalıştırır. Anahtar, kayıt ve sonuç birlikte kaydedilir; hata olursa hiçbiri
    /// kalmaz ve anahtar yeniden kullanılabilir. Anahtarı eşzamanlı başka bir çağrı aldıysa onun sonucu döner.
    /// </summary>
    public async Task<TResult> RunOnceAsync<TResult>(
        string userId, string tool, string key, string fingerprint, Func<Task<TResult>> write) where TResult : class
    {
        DbUpdateException? conflict = null;

        // InMemory (birim testleri) işlem desteklemez. Commit edilmeden çıkılırsa dispose geri alır.
        await using (var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null)
        {
            try
            {
                await SaveAsync(userId, tool, key, Pack<TResult>(fingerprint, null));
            }
            catch (DbUpdateException ex)
            {
                conflict = ex;
            }

            if (conflict is null)
            {
                try
                {
                    var result = await write();
                    var reserved = await db.McpIdempotencyKeys.FindAsync(userId, tool, key);
                    reserved!.ResultJson = Pack(fingerprint, result);
                    await db.SaveChangesAsync();
                    if (transaction is not null)
                    {
                        await transaction.CommitAsync();
                    }

                    return result;
                }
                catch
                {
                    // Başarısız SaveChanges eklenmiş satırları izlemede bırakır; sonraki kayda karışmasınlar.
                    db.ChangeTracker.Clear();
                    if (transaction is null)
                    {
                        await ReleaseAsync(userId, tool, key);
                    }

                    throw;
                }
            }
        }

        // Çakışmalı işlem burada geri alınmış durumda (Npgsql'de hatalı işlemde sorgu çalışmaz); kazanan okunabilir.
        db.ChangeTracker.Clear();
        return await TryReplayAsync<TResult>(userId, tool, key, fingerprint)
            ?? throw new DbUpdateException(conflict.Message, conflict);
    }

    private async Task PurgeExpiredAsync()
    {
        var cutoff = DateTime.UtcNow - Retention;
        var expired = db.McpIdempotencyKeys.Where(x => x.CreatedAt < cutoff);
        if (db.Database.IsRelational())
        {
            // Tek DELETE: aynı satırları eşzamanlı silen iki çağrı birbirini bozmaz.
            await expired.ExecuteDeleteAsync();
            return;
        }

        db.McpIdempotencyKeys.RemoveRange(await expired.ToListAsync());
        await db.SaveChangesAsync();
    }

    private async Task ReleaseAsync(string userId, string tool, string key)
    {
        if (await db.McpIdempotencyKeys.FindAsync(userId, tool, key) is { } reserved)
        {
            db.McpIdempotencyKeys.Remove(reserved);
            await db.SaveChangesAsync();
        }
    }

    private static string Pack<TResult>(string fingerprint, TResult? result) where TResult : class
        => JsonSerializer.Serialize(new StoredResult<TResult>(fingerprint, result), JsonSerializerOptions.Web);

    private sealed record StoredResult<TResult>(string Fingerprint, TResult? Result) where TResult : class;
}
