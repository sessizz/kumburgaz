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

    // Kullanıcının gerçek rolleri (dostane adlarla) — profilde salt-okunur gösterilir.
    // Rol atama yalnızca sistem yöneticisinde (Ayarlar > Kullanıcılar).
    public List<string> Roles { get; set; } = [];

    public class InputModel
    {
        [Required(ErrorMessage = "Kullanıcı adı zorunludur.")]
        [Display(Name = "Kullanıcı Adı")]
        [StringLength(256)]
        public string Username { get; set; } = string.Empty;

        public string? FullName { get; set; }
        public string? Title { get; set; }
        [Phone] public string? PhoneNumber { get; set; }
    }

    private async Task LoadAsync(ApplicationUser user)
    {
        Roles = (await userManager.GetRolesAsync(user)).Select(AppRoles.Display).ToList();
        Input = new InputModel
        {
            Username = await userManager.GetUserNameAsync(user) ?? string.Empty,
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
        ViewData["ActiveTab"] = "profile";
        if (!ModelState.IsValid) { await LoadAsync(user); return Page(); }

        var currentUserName = await userManager.GetUserNameAsync(user);
        var newUserName = Input.Username.Trim();
        if (!string.Equals(currentUserName, newUserName, StringComparison.Ordinal))
        {
            var setNameResult = await userManager.SetUserNameAsync(user, newUserName);
            if (!setNameResult.Succeeded)
            {
                foreach (var error in setNameResult.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);
                await LoadAsync(user);
                return Page();
            }
        }

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
