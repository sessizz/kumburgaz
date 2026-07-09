using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Areas.Mobile.Controllers;

[Area("Mobile")]
[ModuleAuthorize(AppModules.Duyurular)]
public class DuyurularController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var list = await db.Announcements.AsNoTracking()
            .Where(x => x.IsPublished)
            .OrderByDescending(x => x.PublishDate)
            .ToListAsync();
        return View(list);
    }

    public async Task<IActionResult> Detay(int id)
    {
        var announcement = await db.Announcements.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.IsPublished);
        if (announcement is null)
        {
            return NotFound();
        }

        return View(announcement);
    }
}
