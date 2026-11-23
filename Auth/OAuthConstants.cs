using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Auth;

public static class OAuthConstants
{
    // claude.ai bağlayıcısının istediği tek kapsam; /mcp ucu bu kapsamı taşıyan token'ı kabul eder.
    public const string Scope = "mcp";

    public const string McpPolicy = "Mcp";

    // Token'daki Identity güvenlik damgası; şifre değişince ya da sıfırlanınca yenilenir ve eski token'lar reddedilir.
    public const string SecurityStampClaim = "kumburgaz_security_stamp";

    // Dinamik istemci kaydında kabul edilen dönüş adresleri (birebir eşleşme).
    public static readonly IReadOnlySet<string> AllowedRedirectUris = new HashSet<string>(StringComparer.Ordinal)
    {
        "https://claude.ai/api/mcp/auth_callback",
        "https://claude.com/api/mcp/auth_callback"
    };

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
