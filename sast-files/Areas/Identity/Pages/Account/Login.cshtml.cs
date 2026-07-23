using System.ComponentModel.DataAnnotations;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Kumburgaz.Web.Areas.Identity.Pages.Account;

public class LoginModel(SignInManager<ApplicationUser> signInManager) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Kullanıcı adı veya e-posta zorunludur.")]
        [Display(Name = "Kullanıcı adı veya e-posta")]
        public string UserNameOrEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "Şifre zorunludur.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }

    public async Task OnGetAsync(string? returnUrl = null)
    {
        if (!string.IsNullOrEmpty(ErrorMessage))
            ModelState.AddModelError(string.Empty, ErrorMessage);

        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        ReturnUrl = returnUrl ?? Url.Content("~/");
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");

        if (!ModelState.IsValid)
            return Page();

        // Girişte kullanıcı adı veya e-posta kabul edilir; önce kullanıcı adı, sonra e-posta ile aranır.
        var identifier = Input.UserNameOrEmail.Trim();
        var user = await signInManager.UserManager.FindByNameAsync(identifier);
        if (user is null && identifier.Contains('@'))
            user = await signInManager.UserManager.FindByEmailAsync(identifier);

        if (user?.UserName is not null)
        {
            var result = await signInManager.PasswordSignInAsync(
                user.UserName, Input.Password, Input.RememberMe, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                // Açık yönlendirmeyi engelle: yalnızca yerel yollara dön, aksi halde ana sayfaya.
                return Redirect(IsLocalPath(returnUrl) ? returnUrl! : "/");
            }
        }

        ModelState.AddModelError(string.Empty, "Geçersiz kullanıcı adı/e-posta veya şifre.");
        return Page();
    }

    // Yalnızca aynı siteye ait yolları kabul eder (açık yönlendirme koruması). "/", "/Units" yerel; "//x", "http://.." değil.
    private static bool IsLocalPath(string? url)
        => !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
