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
/// refresh) kullanıcı veritabanından yeniden okunur: rolü düşen ya da kilitlenen kullanıcı token alamaz,
/// yeni token eski rolleri taşımaz.
/// </summary>
public class OAuthPrincipalFactory(UserManager<ApplicationUser> users, IConfiguration config)
{
    // Uygun değilse null: rolsüz, yalnız Sakin, kilitli.
    public async Task<ClaimsPrincipal?> CreateAsync(ApplicationUser user, ImmutableArray<string> scopes, string? resource)
    {
        // Audience her zaman kanonik /mcp adresidir. OpenIddict kayıtlı olmayan resource'u zaten
        // invalid_target ile reddeder; buraya başka bir değer gelmesi programlama hatasıdır.
        var mcpResource = config.GetMcpResource();
        if (resource is not null && !string.Equals(resource.TrimEnd('/'), mcpResource, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Desteklenmeyen resource: {resource}", nameof(resource));
        }

        if (await users.IsLockedOutAsync(user))
        {
            return null;
        }

        var roles = await users.GetRolesAsync(user);
        if (!roles.Any(OAuthConstants.EligibleRoles.Contains))
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
            .SetClaims(Claims.Role, [.. roles]);

        identity.SetScopes(scopes);
        identity.SetResources(mcpResource);
        identity.SetDestinations(static _ => [Destinations.AccessToken]);

        return new ClaimsPrincipal(identity);
    }
}
