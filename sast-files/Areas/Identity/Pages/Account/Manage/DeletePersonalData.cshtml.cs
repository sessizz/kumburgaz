using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Kumburgaz.Web.Models;
using System.ComponentModel.DataAnnotations;

namespace Kumburgaz.Web.Areas.Identity.Pages.Account.Manage;

public class DeletePersonalDataModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) : PageModel
{
    [TempData] public string? StatusMessage { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required][DataType(DataType.Password)] public string Password { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null) return NotFound();
        ViewData["ActiveTab"] = "data";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null) return NotFound();
        if (!await userManager.CheckPasswordAsync(user, Input.Password))
        {
            ModelState.AddModelError(string.Empty, "Şifre yanlış.");
            return Page();
        }
        await signInManager.SignOutAsync();
        await userManager.DeleteAsync(user);
        return RedirectToPage("/Account/Login");
    }
}
