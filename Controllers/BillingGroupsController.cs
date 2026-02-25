using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize]
public class BillingGroupsController(
    ApplicationDbContext db,
    IBillingGroupService billingGroupService) : Controller
{
    public async Task<IActionResult> Index()
    {
        return View(await billingGroupService.GetAllAsync());
    }

    public async Task<IActionResult> Create()
    {
        return View(await BuildFormAsync(new BillingGroupFormViewModel
        {
            EffectiveStartPeriod = $"{DateTime.Today:yyyy-MM}",
            Active = true
        }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BillingGroupFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(model));
        }

        try
        {
            await billingGroupService.CreateOrUpdateAsync(model);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(await BuildFormAsync(model));
        }
    }

    public async Task<IActionResult> Edit(int id)
    {
        var group = await billingGroupService.GetByIdAsync(id);
        if (group is null)
        {
            return NotFound();
        }

        var model = new BillingGroupFormViewModel
        {
            Id = group.Id,
            Name = group.Name,
            DuesTypeId = group.DuesTypeId,
            EffectiveStartPeriod = group.EffectiveStartPeriod,
            EffectiveEndPeriod = group.EffectiveEndPeriod,
            Active = group.Active,
            SelectedUnitIds = group.Units.Select(x => x.UnitId).ToList()
        };

        return View(await BuildFormAsync(model));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(BillingGroupFormViewModel model)
    {
        if (!ModelState.IsValid || model.Id is null)
        {
            return View(await BuildFormAsync(model));
        }

        try
        {
            await billingGroupService.CreateOrUpdateAsync(model);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(await BuildFormAsync(model));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        await billingGroupService.DeleteAsync(id);
        return RedirectToAction(nameof(Index));
    }

    private async Task<BillingGroupFormViewModel> BuildFormAsync(BillingGroupFormViewModel model)
    {
        model.DuesTypeOptions = await db.DuesTypes
            .AsNoTracking()
            .Where(x => x.Active)
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name + $" ({x.Amount:N2} TL)", x.Id.ToString()))
            .ToListAsync();

        model.UnitOptions = await db.Units
            .AsNoTracking()
            .Where(x => x.Active)
            .Include(x => x.Block)
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .Select(x => new SelectListItem($"{x.Block!.Name}-{x.UnitNo}", x.Id.ToString()))
            .ToListAsync();

        return model;
    }
}
