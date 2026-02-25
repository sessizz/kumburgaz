using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kumburgaz.Web.Controllers;

[Authorize]
public class DuesGenerationController(IDuesGenerationService duesGenerationService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? period)
    {
        period ??= $"{DateTime.Today:yyyy-MM}";
        var preview = await duesGenerationService.PreviewAsync(period);
        ViewBag.Period = period;
        ViewBag.DueDate = DateTime.Today;
        return View(preview);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(string period, DateTime dueDate)
    {
        await duesGenerationService.GenerateForPeriodAsync(period, dueDate);
        TempData["Success"] = "Donem borclari olusturuldu.";
        return RedirectToAction(nameof(Index), new { period });
    }
}
