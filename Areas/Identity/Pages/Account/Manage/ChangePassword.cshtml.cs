using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Kumburgaz.Web.Models;
using System.ComponentModel.DataAnnotations;

namespace Kumburgaz.Web.Areas.Identity.Pages.Account.Manage;

public class ChangePasswordModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) : PageModel
{
    [TempData] public string? StatusMessage { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required][DataType(DataType.Password)] public string OldPassword { get; set; } = "";
        [Required][MinLength(6)][DataType(DataType.Password)] public string NewPassword { get; set; } = "";
        [Required][DataType(DataType.Password)][Compare("NewPassword", ErrorMessage = "Şifreler eşleşmiyor.")] public string ConfirmPassword { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null) return NotFound();
        ViewData["ActiveTab"] = "password";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = await userManager.GetUserAsync(User);
        if (user == null) return NotFound();
        var result = await userManager.ChangePasswordAsync(user, Input.OldPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return Page();
        }
        await signInManager.RefreshSignInAsync(user);
        StatusMessage = "Şifreniz başarıyla güncellendi.";
        return RedirectToPage();
    }
}
