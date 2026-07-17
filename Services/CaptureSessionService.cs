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

public sealed class CaptureSession
{
    public required string Token { get; init; }
    public required string UserId { get; init; }

    /// <summary>"gider" veya "belge" - hangi form icin baslatildigini belirler.</summary>
    public required string Purpose { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public List<CaptureStagedFile> Files { get; } = [];
}

public sealed class CaptureSessionService(IMemoryCache cache)
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(15);
    private const string KeyPrefix = "capture-session:";

    public CaptureSession Create(string userId, string purpose)
    {
        var token = RandomNumberGenerator.GetHexString(20);
        var session = new CaptureSession { Token = token, UserId = userId, Purpose = purpose };
        cache.Set(KeyPrefix + token, session, SessionLifetime);
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

        if (!cache.TryGetValue(KeyPrefix + token, out CaptureSession? session) || session is null)
        {
            return null;
        }

        return string.Equals(session.UserId, userId, StringComparison.Ordinal) ? session : null;
    }

    public bool AddFile(string? token, string? userId, string fileName, string contentType, byte[] content)
    {
        var session = Get(token, userId);
        if (session is null)
        {
            return false;
        }

        lock (session)
        {
            session.Files.Add(new CaptureStagedFile(Guid.NewGuid(), fileName, contentType, content, DateTime.UtcNow));
        }

        return true;
    }

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

    /// <summary>Oturumu tamamen kapatir (form kaydedildikten sonra cagrilir).</summary>
    public void Remove(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            cache.Remove(KeyPrefix + token);
        }
    }
}
