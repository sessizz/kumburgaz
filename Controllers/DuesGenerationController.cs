using Kumburgaz.Web.Services;
using Kumburgaz.Web.Models;
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
        ViewBag.AccrualDate = new DateTime(startYear, 7, 1);
        ViewBag.DueDate = new DateTime(startYear, 7, 31);
        ViewBag.PayerType = DuesPayerType.Owner;
        ViewBag.PayerTypeOptions = AccountDisplayHelper.PayerTypeOptions(DuesPayerType.Owner);
        return View(preview);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(string period, DateTime accrualDate, DateTime dueDate, DuesPayerType payerType)
    {
        await duesGenerationService.GenerateForPeriodAsync(period, accrualDate, dueDate, payerType);
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
