using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize(Policy = AppPolicies.SystemAdmin)]
public class SystemUsersController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager) : Controller
{
    public async Task<IActionResult> Index()
    {
        var users = await userManager.Users
            .Include(x => x.Account)
            .OrderBy(x => x.Email)
            .ToListAsync();

        var now = DateTimeOffset.UtcNow;
        var rows = new List<SystemUserIndexRowViewModel>();

        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            rows.Add(new SystemUserIndexRowViewModel
            {
                Id = user.Id,
                Email = user.Email ?? user.UserName ?? string.Empty,
                FullName = user.FullName,
                Title = user.Title,
                AccountName = user.Account?.Name,
                RolesText = roles.Count == 0 ? "-" : string.Join(", ", roles),
                IsLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd.Value > now
            });
        }

        return View(rows);
    }

    public async Task<IActionResult> Create()
    {
        return View(await BuildFormAsync(new SystemUserFormViewModel()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SystemUserFormViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError(nameof(model.Password), "Yeni kullanıcı için şifre zorunludur.");
        }

        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(model));
        }

        var user = new ApplicationUser
        {
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            EmailConfirmed = true,
            FullName = model.FullName,
            Title = model.Title,
            AccountId = model.AccountId
        };

        var result = await userManager.CreateAsync(user, model.Password!);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(await BuildFormAsync(model));
        }

        await SyncRolesAsync(user, model.SelectedRoles);
        await SetLockoutAsync(user, model.IsLockedOut);

        TempData["Success"] = "Kullanıcı oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(string id)
    {
        var user = await userManager.Users.Include(x => x.Account).FirstOrDefaultAsync(x => x.Id == id);
        if (user is null)
        {
            return NotFound();
        }

        var roles = await userManager.GetRolesAsync(user);
        var model = new SystemUserFormViewModel
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            Title = user.Title,
            AccountId = user.AccountId,
            IsLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow,
            SelectedRoles = roles.ToList()
        };

        return View(await BuildFormAsync(model));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, SystemUserFormViewModel model)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(model));
        }

        var user = await userManager.FindByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        user.Email = model.Email.Trim();
        user.UserName = model.Email.Trim();
        user.FullName = model.FullName;
        user.Title = model.Title;
        user.AccountId = model.AccountId;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            AddIdentityErrors(updateResult);
            return View(await BuildFormAsync(model));
        }

        if (!string.IsNullOrWhiteSpace(model.Password))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var passwordResult = await userManager.ResetPasswordAsync(user, token, model.Password);
            if (!passwordResult.Succeeded)
            {
                AddIdentityErrors(passwordResult);
                return View(await BuildFormAsync(model));
            }
        }

        await SyncRolesAsync(user, model.SelectedRoles);
        await SetLockoutAsync(user, model.IsLockedOut);

        TempData["Success"] = "Kullanıcı güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<SystemUserFormViewModel> BuildFormAsync(SystemUserFormViewModel model)
    {
        model.RoleOptions = await roleManager.Roles
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem
            {
                Value = x.Name!,
                Text = x.Name!,
                Selected = model.SelectedRoles.Contains(x.Name!)
            })
            .ToListAsync();

        model.AccountOptions = await db.Accounts
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem
            {
                Value = x.Id.ToString(),
                Text = x.Name,
                Selected = model.AccountId == x.Id
            })
            .ToListAsync();

        model.AccountOptions.Insert(0, new SelectListItem("Hesaba bağlı değil", string.Empty));
        return model;
    }

    private async Task SyncRolesAsync(ApplicationUser user, List<string> selectedRoles)
    {
        var currentRoles = await userManager.GetRolesAsync(user);
        var selected = selectedRoles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var toRemove = currentRoles.Except(selected, StringComparer.OrdinalIgnoreCase).ToList();
        var toAdd = selected.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToList();

        if (toRemove.Count > 0)
        {
            await userManager.RemoveFromRolesAsync(user, toRemove);
        }

        if (toAdd.Count > 0)
        {
            await userManager.AddToRolesAsync(user, toAdd);
        }
    }

    private async Task SetLockoutAsync(ApplicationUser user, bool locked)
    {
        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, locked ? DateTimeOffset.UtcNow.AddYears(100) : null);
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
    }
}
