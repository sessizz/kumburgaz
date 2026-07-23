using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Areas.Identity.Pages.Account.Manage;

public class TwoFactorAuthenticationModel(UserManager<ApplicationUser> userManager) : PageModel
{
    public bool Is2faEnabled { get; set; }
    public bool HasAuthenticator { get; set; }
    public int RecoveryCodesLeft { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null) return NotFound();
        Is2faEnabled = await userManager.GetTwoFactorEnabledAsync(user);
        HasAuthenticator = await userManager.GetAuthenticatorKeyAsync(user) != null;
        ViewData["ActiveTab"] = "2fa";
        return Page();
    }

    public async Task<IActionResult> OnPostDisableAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null) return NotFound();
        await userManager.SetTwoFactorEnabledAsync(user, false);
        return RedirectToPage();
    }
}
