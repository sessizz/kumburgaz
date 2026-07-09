using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Kumburgaz.Web.Areas.Mobile.Controllers;

// Sakin'in bu modul icin gorunum yetkisi yok (SeedRolePermissionsAsync) — masaustuyle ayni.
[Area("Mobile")]
[ModuleAuthorize(AppModules.KasaBanka)]
public class KasaBankaController(
    ApplicationDbContext db,
    CashBankDetailService detailService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var items = await CashBankBalanceHelper.BuildAsync(db);
        return View(new MobileKasaBankaListViewModel { Items = items });
    }

    public async Task<IActionResult> Detay(string kind, int id)
    {
        if (kind is not ("cash" or "bank"))
        {
            return NotFound();
        }

        var vm = await detailService.BuildAsync(kind, id, new CashBankDetailQuery());
        if (vm is null)
        {
            return NotFound();
        }

        ViewData["Title"] = vm.Name;
        return View(new MobileKasaBankaDetayViewModel
        {
            Kind = vm.Kind,
            Id = vm.Id,
            Name = vm.Name,
            Balance = vm.Balance,
            MonthInflow = vm.MonthInflow,
            MonthOutflow = vm.MonthOutflow,
            Groups = vm.Groups
        });
    }
}
