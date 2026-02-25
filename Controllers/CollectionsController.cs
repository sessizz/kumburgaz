using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize]
public class CollectionsController(
    ApplicationDbContext db,
    ICollectionService collectionService) : Controller
{
    public async Task<IActionResult> Index()
    {
        return View(await collectionService.GetAllAsync());
    }

    public async Task<IActionResult> Create()
    {
        return View(await BuildFormAsync(new CollectionCreateViewModel
        {
            Date = DateTime.Today,
            PaymentChannel = PaymentChannel.Bank
        }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CollectionCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(model));
        }

        try
        {
            await collectionService.CreateAsync(model);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(await BuildFormAsync(model));
        }
    }

    private async Task<CollectionCreateViewModel> BuildFormAsync(CollectionCreateViewModel model)
    {
        model.BillingGroupOptions = await db.BillingGroups
            .AsNoTracking()
            .Where(x => x.Active)
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString()))
            .ToListAsync();

        return model;
    }
}
