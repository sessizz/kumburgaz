using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Kumburgaz.Web.Models;
using System.ComponentModel.DataAnnotations;

namespace Kumburgaz.Web.Areas.Identity.Pages.Account.Manage;

public class EmailModel(UserManager<ApplicationUser> userManager) : PageModel
{
    [TempData] public string? StatusMessage { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public string? Email { get; set; }
    public bool IsEmailConfirmed { get; set; }

    public class InputModel
    {
        [Required][EmailAddress] public string? NewEmail { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null) return NotFound();
        Email = await userManager.GetEmailAsync(user);
        IsEmailConfirmed = await userManager.IsEmailConfirmedAsync(user);
        ViewData["ActiveTab"] = "email";
        return Page();
    }

    public async Task<IActionResult> OnPostChangeEmailAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null) return NotFound();
        if (!ModelState.IsValid) { Email = await userManager.GetEmailAsync(user); return Page(); }
        // In a real app send confirmation email; for now just update directly
        var token = await userManager.GenerateChangeEmailTokenAsync(user, Input.NewEmail!);
        var result = await userManager.ChangeEmailAsync(user, Input.NewEmail!, token);
        if (result.Succeeded)
        {
            await userManager.SetUserNameAsync(user, Input.NewEmail);
            StatusMessage = "E-posta adresiniz güncellendi.";
        }
        else
            StatusMessage = "Hata: E-posta güncellenemedi.";
        return RedirectToPage();
    }
}
