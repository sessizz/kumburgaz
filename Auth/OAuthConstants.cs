using System.Diagnostics.CodeAnalysis;
using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Auth;

public static class OAuthConstants
{
    // claude.ai bağlayıcısının istediği tek kapsam; /mcp ucu bu kapsamı taşıyan token'ı kabul eder.
    public const string Scope = "mcp";

    public const string McpPolicy = "Mcp";

    // Token'daki Identity güvenlik damgası; şifre değişince ya da sıfırlanınca yenilenir ve eski token'lar reddedilir.
    public const string SecurityStampClaim = "kumburgaz_security_stamp";

    // claude.ai / claude.com bağlayıcılarının dönüş adresleri (birebir eşleşme).
    public static readonly IReadOnlySet<string> ClaudeRedirectUris = new HashSet<string>(StringComparer.Ordinal)
    {
        "https://claude.ai/api/mcp/auth_callback",
        "https://claude.com/api/mcp/auth_callback"
    };

    // Masaüstü/CLI istemcilerinin (ör. Hermes) yerel geri dönüş sunucusu (RFC 8252 §7.3).
    private static readonly IReadOnlySet<string> LoopbackHosts = new HashSet<string>(StringComparer.Ordinal)
    {
        "127.0.0.1",
        "[::1]",
        "localhost"
    };

    // Dinamik istemci kaydında kabul edilen dönüş adresi: Claude adreslerinden biri ya da bu
    // bilgisayarın kendisine (loopback) giden düz http adres. Yetki kodu yerel adrese gittiği için
    // başka bir makine ele geçiremez; PKCE zaten zorunlu. Kayıttan sonra authorize/token isteğindeki
    // adres OpenIddict tarafından kayıtlı adresle birebir karşılaştırılır (port dahil).
    public static bool IsAllowedRedirectUri([NotNullWhen(true)] string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (ClaudeRedirectUris.Contains(value))
        {
            return true;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttp
            && uri.UserInfo.Length == 0
            && uri.Fragment.Length == 0
            && LoopbackHosts.Contains(uri.Host);
    }

    // Bağlayıcıyı kullanabilen roller; Sakin ve rolsüz kullanıcılar reddedilir.
    public static readonly IReadOnlySet<string> EligibleRoles = new HashSet<string>(StringComparer.Ordinal)
    {
        AppRoles.SistemYonetici,
        AppRoles.SiteYonetici,
        AppRoles.MuhasebeGorevli,
        AppRoles.Personel,
        AppRoles.SadeceGoruntuleme
    };

    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
}
