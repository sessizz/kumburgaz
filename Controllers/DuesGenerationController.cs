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
        period ??= PeriodHelper.CurrentFiscalPeriod(DateTime.Today);
        var preview = await duesGenerationService.PreviewAsync(period);
        ViewBag.Period = period;
        var startYear = int.Parse(period[..4]);
        ViewBag.DueDate = new DateTime(startYear, 7, 31);
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string period)
    {
        try
        {
            await duesGenerationService.DeleteForPeriodAsync(period);
            TempData["Success"] = "Donem borclari silindi.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), new { period });
    }
}
