using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Kumburgaz.Web.Models;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;

namespace Kumburgaz.Web.Areas.Identity.Pages.Account.Manage;

public class EnableAuthenticatorModel(UserManager<ApplicationUser> userManager, UrlEncoder urlEncoder) : PageModel
{
    [TempData] public string? StatusMessage { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public string? SharedKey { get; set; }
    public string? AuthenticatorUri { get; set; }

    public class InputModel
    {
        [Required][StringLength(7, MinimumLength = 6)][DataType(DataType.Text)] public string Code { get; set; } = "";
    }

    private async Task LoadSharedKeyAndQrCodeUriAsync(ApplicationUser user)
    {
        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }
        SharedKey = FormatKey(key!);
        var email = await userManager.GetEmailAsync(user);
        AuthenticatorUri = $"otpauth://totp/{urlEncoder.Encode("Kumburgaz")}:{urlEncoder.Encode(email!)}?secret={key}&issuer={urlEncoder.Encode("Kumburgaz")}";
    }

    private static string FormatKey(string unformattedKey)
    {
        var result = new StringBuilder();
        for (int i = 0; i < unformattedKey.Length; i++)
        {
            if (i > 0 && i % 4 == 0) result.Append(' ');
            result.Append(unformattedKey[i]);
        }
        return result.ToString().ToUpperInvariant();
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null) return NotFound();
        await LoadSharedKeyAndQrCodeUriAsync(user);
        ViewData["ActiveTab"] = "2fa";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null) return NotFound();
        if (!ModelState.IsValid) { await LoadSharedKeyAndQrCodeUriAsync(user); return Page(); }
        var code = Input.Code.Replace(" ", "").Replace("-", "");
        var is2faTokenValid = await userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider, code);
        if (!is2faTokenValid)
        {
            ModelState.AddModelError("Input.Code", "Doğrulama kodu geçersiz.");
            await LoadSharedKeyAndQrCodeUriAsync(user);
            return Page();
        }
        await userManager.SetTwoFactorEnabledAsync(user, true);
        StatusMessage = "İki adımlı doğrulama etkinleştirildi.";
        return RedirectToPage("./TwoFactorAuthentication");
    }
}
