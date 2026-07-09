using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Areas.Mobile.Controllers;

[Area("Mobile")]
[ModuleAuthorize(AppModules.Talepler)]
public class TaleplerController(ApplicationDbContext db, MobileScopeService scope) : Controller
{
    public async Task<IActionResult> Index()
    {
        var isResident = scope.IsResident(User);

        var query = db.ServiceRequests.AsNoTracking()
            .Include(x => x.Unit).ThenInclude(x => x!.Block)
            .AsQueryable();

        // Sakin yalnızca "sakinlere görünür" talepleri görür.
        if (isResident)
        {
            query = query.Where(x => x.IsVisibleToResidents);
        }

        var list = await query
            .OrderBy(x => x.Status == ServiceRequestStatus.Closed)
            .ThenByDescending(x => x.Priority)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync();

        ViewBag.IsResident = isResident;
        return View(list);
    }

    public async Task<IActionResult> Detay(int id)
    {
        var isResident = scope.IsResident(User);
        var talep = await db.ServiceRequests.AsNoTracking()
            .Include(x => x.Unit).ThenInclude(x => x!.Block)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (talep is null || (isResident && !talep.IsVisibleToResidents))
        {
            return NotFound();
        }

        return View(talep);
    }

    [HttpGet]
    public async Task<IActionResult> Yeni()
    {
        return View(await BuildFormAsync(new MobileTalepFormViewModel()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Yeni(MobileTalepFormViewModel model)
    {
        var isResident = scope.IsResident(User);
        var allowedUnitIds = await scope.GetAllowedUnitIdsAsync(User);

        // Sakin yalnızca erişimindeki daireyi seçebilir.
        if (isResident && model.UnitId.HasValue && (allowedUnitIds is null || !allowedUnitIds.Contains(model.UnitId.Value)))
        {
            ModelState.AddModelError(nameof(model.UnitId), "Yalnızca kendi dairelerinizi seçebilirsiniz.");
        }

        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(model));
        }

        var request = new ServiceRequest
        {
            Title = model.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim(),
            UnitId = model.UnitId,
            Status = ServiceRequestStatus.Open,
            Priority = ServiceRequestPriority.Normal,
            CreatedAt = DateTime.UtcNow,
            // Sakinin açtığı talep her zaman kendisine görünür; yönetici seçime bırakır.
            IsVisibleToResidents = isResident || model.IsVisibleToResidents
        };

        db.ServiceRequests.Add(request);
        await db.SaveChangesAsync();
        TempData["MobileSuccess"] = "Talebiniz oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<MobileTalepFormViewModel> BuildFormAsync(MobileTalepFormViewModel model)
    {
        model.IsResident = scope.IsResident(User);
        var allowedUnitIds = await scope.GetAllowedUnitIdsAsync(User);

        var unitsQuery = db.Units.AsNoTracking().Include(x => x.Block).Where(x => x.Active);
        if (allowedUnitIds is not null)
        {
            unitsQuery = unitsQuery.Where(x => allowedUnitIds.Contains(x.Id));
        }

        model.UnitOptions = await unitsQuery
            .OrderBy(x => x.Block!.Name).ThenBy(x => x.UnitNo)
            .Select(x => new SelectListItem($"{x.Block!.Name}-{x.UnitNo}", x.Id.ToString()))
            .ToListAsync();

        return model;
    }
}
