using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Kumburgaz.Web.Models;
using System.ComponentModel.DataAnnotations;

namespace Kumburgaz.Web.Areas.Identity.Pages.Account.Manage;

public class IndexModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) : PageModel
{
    [TempData] public string? StatusMessage { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();

    public string? Username { get; set; }

    public class InputModel
    {
        public string? FullName { get; set; }
        public string? Title { get; set; }
        [Phone] public string? PhoneNumber { get; set; }
    }

    private async Task LoadAsync(ApplicationUser user)
    {
        Username = await userManager.GetUserNameAsync(user);
        Input = new InputModel
        {
            FullName = user.FullName,
            Title = user.Title,
            PhoneNumber = await userManager.GetPhoneNumberAsync(user)
        };
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null) return NotFound();
        await LoadAsync(user);
        ViewData["ActiveTab"] = "profile";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null) return NotFound();
        if (!ModelState.IsValid) { await LoadAsync(user); return Page(); }

        user.FullName = Input.FullName;
        user.Title = Input.Title;
        var phoneNumber = await userManager.GetPhoneNumberAsync(user);
        if (Input.PhoneNumber != phoneNumber)
            await userManager.SetPhoneNumberAsync(user, Input.PhoneNumber);
        await userManager.UpdateAsync(user);
        await signInManager.RefreshSignInAsync(user);
        StatusMessage = "Profiliniz güncellendi.";
        return RedirectToPage();
    }
}
