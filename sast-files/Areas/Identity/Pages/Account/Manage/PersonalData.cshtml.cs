using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Areas.Identity.Pages.Account.Manage;

public class PersonalDataModel(UserManager<ApplicationUser> userManager) : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null) return NotFound();
        ViewData["ActiveTab"] = "data";
        return Page();
    }
}
