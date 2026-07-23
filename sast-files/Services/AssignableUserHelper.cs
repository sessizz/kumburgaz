using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

/// <summary>
/// Talep atama dropdown'u icin personel/yonetici kullanicilarini listeler.
/// Sakin kullanicilari (daire sahibi/kiraci girisleri) atama listesine hic girmez.
/// </summary>
public static class AssignableUserHelper
{
    public static async Task<List<SelectListItem>> BuildOptionsAsync(UserManager<ApplicationUser> userManager, string? selectedUserId)
    {
        var users = await userManager.Users.OrderBy(x => x.UserName).ToListAsync();
        var options = new List<SelectListItem> { new("Atanmadı", string.Empty, string.IsNullOrEmpty(selectedUserId)) };

        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            if (roles.Contains(AppRoles.Sakin))
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(user.FullName) ? (user.Email ?? user.UserName ?? user.Id) : user.FullName;
            options.Add(new SelectListItem(label, user.Id, user.Id == selectedUserId));
        }

        return options;
    }

    public static string DisplayName(ApplicationUser user)
        => string.IsNullOrWhiteSpace(user.FullName) ? (user.Email ?? user.UserName ?? user.Id) : user.FullName;
}
