using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize(Policy = AppPolicies.SystemAdmin)]
public class RolePermissionsController(ApplicationDbContext db, PermissionService permissions) : Controller
{
    // SistemYonetici matriste düzenlenmez; kod tarafında her zaman tam yetkilidir.
    private static IEnumerable<string> ConfigurableRoles =>
        AppRoles.All.Where(r => r != AppRoles.SistemYonetici);

    public async Task<IActionResult> Index()
    {
        var rows = await db.RolePermissions.AsNoTracking().ToListAsync();
        var map = rows.ToDictionary(x => (x.RoleName, x.Module), x => x);

        var model = new RolePermissionMatrixViewModel
        {
            Roles = ConfigurableRoles.ToList()
        };

        foreach (var module in AppModules.All)
        {
            var row = new RolePermissionModuleRow
            {
                Module = module.Key,
                DisplayName = module.DisplayName,
                Icon = module.Icon
            };

            foreach (var role in model.Roles)
            {
                var cell = new RolePermissionCell();
                if (map.TryGetValue((role, module.Key), out var existing))
                {
                    cell.CanView = existing.CanView;
                    cell.CanWrite = existing.CanWrite;
                }

                row.Cells[role] = cell;
            }

            model.Modules.Add(row);
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(IFormCollection form)
    {
        var existing = await db.RolePermissions.ToListAsync();
        var map = existing.ToDictionary(x => (x.RoleName, x.Module), x => x);

        foreach (var role in ConfigurableRoles)
        {
            foreach (var module in AppModules.All)
            {
                var write = form[$"write_{role}_{module.Key}"].Contains("true");
                // Yazma yetkisi görüntülemeyi de kapsar.
                var view = write || form[$"view_{role}_{module.Key}"].Contains("true");

                if (map.TryGetValue((role, module.Key), out var rp))
                {
                    if (!view && !write)
                    {
                        db.RolePermissions.Remove(rp);
                    }
                    else
                    {
                        rp.CanView = view;
                        rp.CanWrite = write;
                    }
                }
                else if (view || write)
                {
                    db.RolePermissions.Add(new RolePermission
                    {
                        RoleName = role,
                        Module = module.Key,
                        CanView = view,
                        CanWrite = write
                    });
                }
            }
        }

        await db.SaveChangesAsync();
        permissions.Invalidate();

        TempData["Success"] = "Rol yetkileri güncellendi.";
        return RedirectToAction(nameof(Index));
    }
}
