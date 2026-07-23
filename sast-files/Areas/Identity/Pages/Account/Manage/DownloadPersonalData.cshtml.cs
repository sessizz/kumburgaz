using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Kumburgaz.Web.Models;
using System.Text.Json;

namespace Kumburgaz.Web.Areas.Identity.Pages.Account.Manage;

public class DownloadPersonalDataModel(UserManager<ApplicationUser> userManager) : PageModel
{
    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null) return NotFound();
        var personalData = new Dictionary<string, string>
        {
            ["Email"] = user.Email ?? "",
            ["UserName"] = user.UserName ?? "",
            ["FullName"] = user.FullName ?? "",
            ["PhoneNumber"] = user.PhoneNumber ?? "",
        };
        return File(JsonSerializer.SerializeToUtf8Bytes(personalData, new JsonSerializerOptions { WriteIndented = true }), "application/json", "PersonalData.json");
    }
}
