using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using ModelContextProtocol;

namespace Kumburgaz.Web.Mcp;

/// <summary>
/// Önizlemenin alındığının kanıtı. confirm: false bir token döner; confirm: true yalnız aynı kullanıcı, araç ve
/// girdiler için verilmiş, süresi dolmamış token'la yazar. Böylece önizleme adımı atlanamaz.
/// </summary>
public sealed class McpPreviewTokens(IDataProtectionProvider dataProtection)
{
    public const string Purpose = "Kumburgaz.Mcp.PreviewToken";

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly ITimeLimitedDataProtector _protector = dataProtection.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public string Issue(string userId, string tool, string fingerprint)
        => _protector.Protect(Payload(userId, tool, fingerprint), Lifetime);

    public void Require(string? token, string userId, string tool, string fingerprint)
    {
        if (!IsValid(token, Payload(userId, tool, fingerprint)))
        {
            throw new McpException("Önce confirm: false ile önizleme alın; girdiler değiştiyse yeniden önizleyin.");
        }
    }

    private bool IsValid(string? token, string expectedPayload)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            return string.Equals(_protector.Unprotect(token.Trim()), expectedPayload, StringComparison.Ordinal);
        }
        catch (CryptographicException)
        {
            // Bozuk, başka amaçla üretilmiş ya da süresi dolmuş token.
            return false;
        }
    }

    private static string Payload(string userId, string tool, string fingerprint) => $"{userId}\n{tool}\n{fingerprint}";
}
