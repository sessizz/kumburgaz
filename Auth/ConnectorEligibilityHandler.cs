using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;

namespace Kumburgaz.Web.Auth;

// Token'ın kullanıcısı şu anda da bağlayıcıyı kullanabilmeli.
public sealed class ConnectorEligibilityRequirement : IAuthorizationRequirement;

/// <summary>
/// Kullanıcıyı her istekte veritabanından yeniden okur: silinen, kilitlenen, şifresi değişen ya da uygunluğunu yitiren
/// kullanıcının geçerli token'ı süresi dolmadan reddedilir. Token'daki roller değişmez; uygun roller
/// arasındaki değişiklik token yenilenince etkili olur.
/// </summary>
public sealed class ConnectorEligibilityHandler(UserManager<ApplicationUser> users, OAuthPrincipalFactory principals)
    : AuthorizationHandler<ConnectorEligibilityRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ConnectorEligibilityRequirement requirement)
    {
        var userId = context.User.GetClaim(OpenIddictConstants.Claims.Subject);
        var user = userId is null ? null : await users.FindByIdAsync(userId);
        if (user is not null && await principals.IsEligibleAsync(user, context.User))
        {
            context.Succeed(requirement);
        }
    }
}
