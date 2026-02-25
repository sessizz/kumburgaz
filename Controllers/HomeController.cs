using System.Diagnostics;
using Kumburgaz.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Controllers;

public class HomeController(ApplicationDbContext db) : Controller
{
    public IActionResult Index()
    {
        ViewBag.TotalDebt = db.DuesInstallments.Sum(x => x.RemainingAmount);
        ViewBag.TotalGenerated = db.DuesInstallments.Sum(x => x.Amount);
        ViewBag.TotalCollections = db.Collections.Sum(x => x.Amount);
        ViewBag.BillingGroups = db.BillingGroups.Count(x => x.Active);
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
