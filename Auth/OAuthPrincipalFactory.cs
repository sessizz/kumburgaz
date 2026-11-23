using System.Collections.Immutable;
using System.Security.Claims;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kumburgaz.Web.Auth;

/// <summary>
/// Bağlayıcı token'larındaki kullanıcı kimliğini kurar. Onayda ve her token alışında (code ya da
/// refresh) kullanıcı veritabanından yeniden okunur: rolü düşen, kilitlenen ya da şifresi değişen kullanıcı token
/// alamaz, yeni token eski rolleri taşımaz.
/// </summary>
public class OAuthPrincipalFactory(UserManager<ApplicationUser> users, IConfiguration config)
{
    // Verilmiş token (code, refresh ya da access) kullanıcı için hâlâ geçerli mi? Kuralın tek kaynağı: token ucu ve her
    // /mcp isteği. Kullanıcı uygun kalmalı, güvenlik damgası da token'dakiyle aynı olmalı: şifre değişince ya da
    // sıfırlanınca damga yenilenir ve önceki bağlantılar kopar. Damgasız token da reddedilir.
    public async Task<bool> IsEligibleAsync(ApplicationUser user, ClaimsPrincipal token)
        => token.GetClaim(OAuthConstants.SecurityStampClaim) is { Length: > 0 } stamp
            && string.Equals(stamp, user.SecurityStamp, StringComparison.Ordinal)
            && await GetEligibleRolesAsync(user) is not null;

    // Uygun değilse null (bkz. GetEligibleRolesAsync).
    public async Task<ClaimsPrincipal?> CreateAsync(ApplicationUser user, ImmutableArray<string> scopes, string? resource)
    {
        // Audience her zaman kanonik /mcp adresidir. OpenIddict kayıtlı olmayan resource'u zaten
        // invalid_target ile reddeder; buraya başka bir değer gelmesi programlama hatasıdır.
        var mcpResource = config.GetMcpResource();
        if (resource is not null && !string.Equals(resource.TrimEnd('/'), mcpResource, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Desteklenmeyen resource: {resource}", nameof(resource));
        }

        var roles = await GetEligibleRolesAsync(user);
        if (roles is null)
        {
            return null;
        }

        var displayName = string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName;
        var identity = new ClaimsIdentity(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: Claims.Name,
            roleType: Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id)
            .SetClaim(ClaimTypes.NameIdentifier, user.Id)
            .SetClaim(Claims.Name, user.UserName)
            // AuditLog bu adı okur; "(Claude)" eki MCP kaynaklı kayıtları ayırt eder.
            .SetClaim(ApplicationUserClaimsPrincipalFactory.DisplayNameClaimType, $"{displayName} (Claude)")
            .SetClaim(OAuthConstants.SecurityStampClaim, user.SecurityStamp)
            .SetClaims(Claims.Role, [.. roles]);

        identity.SetScopes(scopes);
        identity.SetResources(mcpResource);
        // Yalnız şifreli (JWE) access token; güvenlik damgası id_token'a ya da yanıta çıkmamalı.
        identity.SetDestinations(static _ => [Destinations.AccessToken]);

        return new ClaimsPrincipal(identity);
    }

    // Uygun kullanıcının rolleri; kilitli, personel rolü olmayan ya da Sakin rolü taşıyan kullanıcıda null.
    // Sakin, personel rolü de olsa ekranda hiçbir masaüstü sayfasına giremez (SakinAreaRestrictionFilter);
    // Claude kullanıcının ekranda yapamadığını yapamaz.
    private async Task<IList<string>?> GetEligibleRolesAsync(ApplicationUser user)
    {
        if (await users.IsLockedOutAsync(user))
        {
            return null;
        }

        var roles = await users.GetRolesAsync(user);
        return roles.Contains(AppRoles.Sakin) || !roles.Any(OAuthConstants.EligibleRoles.Contains) ? null : roles;
    }
}
