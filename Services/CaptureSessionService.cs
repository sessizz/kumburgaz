using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace Kumburgaz.Web.Services;

/// <summary>
/// Masaustunde "Telefondan ekle" ile baslatilan gecici eslesme oturumu.
/// Telefonda yuklenen dosyalar, masaustundeki form kaydedilene kadar (~15dk)
/// burada bellek-ici tutulur; kalici depoya (Attachment) sadece form basariyla
/// kaydedildiginde yazilir. Tekil-instance mimari ile tutarli (bkz. PushQueue).
/// </summary>
public sealed record CaptureStagedFile(Guid Id, string FileName, string ContentType, byte[] Content, DateTime AddedAt);

public enum CaptureAddFileResult
{
    Success,
    SessionNotFound,
    TooManyFiles,
    SessionTooLarge
}

public sealed class CaptureSession
{
    public required string Token { get; init; }
    public required string UserId { get; init; }

    /// <summary>"gider" veya "belge" - hangi form icin baslatildigini belirler.</summary>
    public required string Purpose { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public List<CaptureStagedFile> Files { get; } = [];

    /// <summary>Masaustu form kaydedilirken TakeFiles ile "muhurlenir"; bu noktadan
    /// sonra gelen AddFile cagrilari reddedilir (kaybolan dosya yerine acik hata).</summary>
    public bool IsConsumed { get; set; }
}

/// <summary>
/// Oturumlari ozel (paylasilmayan) bir MemoryCache'te tutar: boylece SizeLimit sadece
/// bu servisi baglar, uygulamanin paylasilan IMemoryCache'ini kullanan diger servisleri
/// (orn. PermissionService - Size atanmamis girdiler) etkilemez/bozmaz.
/// </summary>
public sealed class CaptureSessionService : IDisposable
{
    private const string KeyPrefix = "capture-session:";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Bir oturumda en fazla kac dosya tutulabilir.</summary>
    public const int MaxFilesPerSession = 20;

    /// <summary>Bir oturumun toplam bayt boyutu ust siniri.</summary>
    public const long MaxSessionBytes = 40L * 1024 * 1024;

    /// <summary>Tum oturumlarin toplam boyutu ust siniri - bellek tasmasini onler;
    /// asilinca cache en eski/dusuk oncelikli girdileri otomatik tahliye eder.</summary>
    private const long TotalCacheSizeLimit = 300L * 1024 * 1024;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = TotalCacheSizeLimit });

    public CaptureSession Create(string userId, string purpose)
    {
        var token = RandomNumberGenerator.GetHexString(20);
        var session = new CaptureSession { Token = token, UserId = userId, Purpose = purpose };
        Store(token, session, 1);
        return session;
    }

    /// <summary>
    /// Oturumu token ile bulur; kullanici eslesmiyorsa (veya oturum yoksa/suresi
    /// dolmussa) null doner. Guvenlik: cagiran her zaman kendi UserId'sini gecmeli.
    /// </summary>
    public CaptureSession? Get(string? token, string? userId)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        if (!_cache.TryGetValue(KeyPrefix + token, out CaptureSession? session) || session is null)
        {
            return null;
        }

        return string.Equals(session.UserId, userId, StringComparison.Ordinal) ? session : null;
    }

    public CaptureAddFileResult AddFile(string? token, string? userId, string fileName, string contentType, byte[] content)
    {
        var session = Get(token, userId);
        if (session is null)
        {
            return CaptureAddFileResult.SessionNotFound;
        }

        lock (session)
        {
            // TakeFiles ile ayni kilit uzerinde calisir: form kaydedilmeye baslandiktan
            // sonra gelen gec bir yukleme burada acikca reddedilir, sessizce kaybolmaz.
            if (session.IsConsumed)
            {
                return CaptureAddFileResult.SessionNotFound;
            }

            if (session.Files.Count >= MaxFilesPerSession)
            {
                return CaptureAddFileResult.TooManyFiles;
            }

            var currentTotal = session.Files.Sum(x => (long)x.Content.Length);
            if (currentTotal + content.Length > MaxSessionBytes)
            {
                return CaptureAddFileResult.SessionTooLarge;
            }

            session.Files.Add(new CaptureStagedFile(Guid.NewGuid(), fileName, contentType, content, DateTime.UtcNow));
            Store(token!, session, currentTotal + content.Length);
        }

        return CaptureAddFileResult.Success;
    }

    /// <summary>Salt-okunur onizleme (masaustu polling ve telefon sayfasi listelemesi icin) - oturumu tuketmez.</summary>
    public IReadOnlyList<CaptureStagedFile> ListFiles(string? token, string? userId)
    {
        var session = Get(token, userId);
        if (session is null)
        {
            return [];
        }

        lock (session)
        {
            return session.Files.ToList();
        }
    }

    public CaptureStagedFile? GetFile(string? token, string? userId, Guid fileId)
    {
        var session = Get(token, userId);
        if (session is null)
        {
            return null;
        }

        lock (session)
        {
            return session.Files.FirstOrDefault(x => x.Id == fileId);
        }
    }

    public bool RemoveFile(string? token, string? userId, Guid fileId)
    {
        var session = Get(token, userId);
        if (session is null)
        {
            return false;
        }

        lock (session)
        {
            return session.Files.RemoveAll(x => x.Id == fileId) > 0;
        }
    }

    /// <summary>
    /// Oturumu "muhurler" (yeni AddFile cagrilarini reddeder), o anki dosyalarin
    /// anlik goruntusunu doner ve oturumu kaldirir - hepsi tek kilit altinda, boylece
    /// es zamanli bir AddFile ya bu snapshot'tan once biter ya da acikca reddedilir.
    /// Form basariyla kaydedildiginde (Ledger/Documents controller) cagrilir.
    /// </summary>
    public IReadOnlyList<CaptureStagedFile> TakeFiles(string? token, string? userId)
    {
        var session = Get(token, userId);
        if (session is null)
        {
            return [];
        }

        lock (session)
        {
            session.IsConsumed = true;
            var snapshot = session.Files.ToList();
            _cache.Remove(KeyPrefix + token!);
            return snapshot;
        }
    }

    private void Store(string token, CaptureSession session, long approximateSize)
    {
        _cache.Set(KeyPrefix + token, session, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = SessionLifetime,
            Size = Math.Max(1, approximateSize)
        });
    }

    public void Dispose() => _cache.Dispose();
}
