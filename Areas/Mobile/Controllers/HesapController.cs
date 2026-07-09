using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Kumburgaz.Web.Areas.Mobile.Controllers;

[Area("Mobile")]
[Authorize]
public class HesapController(
    UserManager<ApplicationUser> userManager,
    ResidentAccountService residentAccountService) : Controller
{
    // "Diğer" sekmesi: menü (şifre değiştir, çıkış, masaüstü linki).
    public IActionResult Diger() => View();

    [HttpGet]
    public IActionResult Sifre() => View(new MobileChangePasswordViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sifre(MobileChangePasswordViewModel model)
    {
        if (model.NewPin != model.ConfirmPin)
        {
            ModelState.AddModelError(nameof(model.ConfirmPin), "PIN tekrarı eşleşmiyor.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var result = await residentAccountService.ChangeOwnPasswordAsync(user, model.CurrentPin, model.NewPin);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Mevcut PIN hatalı olabilir. Lütfen tekrar deneyin.");
            return View(model);
        }

        TempData["MobileSuccess"] = "PIN'iniz güncellendi.";
        return RedirectToAction(nameof(Diger));
    }
}
