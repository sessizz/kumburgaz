using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ModelContextProtocol;

namespace Kumburgaz.Web.Mcp;

// Geçerli bir önizleme token'ının içeriği. Nonce önizlemeyi tanır: bir önizleme bir kez yazar (bkz. McpIdempotencyStore).
public sealed record PreviewApproval(string Nonce, DateTimeOffset ExpiresAt)
{
    public void RequireUnexpired()
    {
        if (DateTimeOffset.UtcNow >= ExpiresAt)
        {
            throw McpPreviewTokens.Rejected();
        }
    }
}

/// <summary>
/// Önizlemenin alındığının kanıtı. confirm: false bir token döner; confirm: true yalnız aynı kullanıcı, araç ve
/// girdiler için verilmiş, süresi dolmamış token'la yazar. Böylece önizleme adımı atlanamaz.
/// </summary>
public sealed class McpPreviewTokens(IDataProtectionProvider dataProtection)
{
    public const string Purpose = "Kumburgaz.Mcp.PreviewToken";

    // McpIdempotencyStore'un saklama süresinden kısa kalmalı: nonce satırı silinene kadar token çoktan geçersizdir.
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly IDataProtector _protector = dataProtection.CreateProtector(Purpose);

    public string Issue(string userId, string tool, string fingerprint)
        => _protector.Protect(JsonSerializer.Serialize(new Payload(
            userId, tool, fingerprint, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), DateTimeOffset.UtcNow + Lifetime)));

    // Süre burada denetlenmez (bkz. PreviewApproval.RequireUnexpired): süresi dolmuş token yazdırmaz, ama daha önce
    // yazdığı kaydın sonucu bulunabilsin.
    public PreviewApproval Read(string? token, string userId, string tool, string fingerprint)
    {
        var payload = Open(token);
        return payload is not null && payload.UserId == userId && payload.Tool == tool && payload.Fingerprint == fingerprint
            ? new PreviewApproval(payload.Nonce, payload.ExpiresAt)
            : throw Rejected();
    }

    internal static McpException Rejected()
        => new("Önce confirm: false ile önizleme alın; girdiler değiştiyse yeniden önizleyin.");

    private Payload? Open(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Payload>(_protector.Unprotect(token.Trim()));
        }
        catch (CryptographicException)
        {
            // Bozuk ya da başka amaçla üretilmiş token.
            return null;
        }
    }

    private sealed record Payload(string UserId, string Tool, string Fingerprint, string Nonce, DateTimeOffset ExpiresAt);
}
