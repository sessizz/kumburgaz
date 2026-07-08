using System.Security.Claims;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Kumburgaz.Web.Services;

public class ApplicationUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options,
    ApplicationDbContext db) : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    public const string DisplayNameClaimType = "kumburgaz_display_name";
    public const string AccountIdClaimType = "kumburgaz_account_id";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        var displayName = user.FullName;

        if (user.AccountId.HasValue)
        {
            var account = await db.Accounts.FindAsync(user.AccountId.Value);
            if (account is not null)
            {
                displayName = account.Name;
                identity.AddClaim(new Claim(AccountIdClaimType, account.Id.ToString()));
            }
        }

        displayName = string.IsNullOrWhiteSpace(displayName)
            ? user.Email ?? user.UserName ?? "Kullanıcı"
            : displayName;

        identity.AddClaim(new Claim(DisplayNameClaimType, displayName));
        return identity;
    }
}
