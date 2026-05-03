using System.Diagnostics;
using Kumburgaz.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;

namespace Kumburgaz.Web.Controllers;

public class HomeController(ApplicationDbContext db) : Controller
{
    public IActionResult Index()
    {
        ViewBag.TotalDebt = db.DuesInstallments.Sum(x => x.RemainingAmount);
        ViewBag.TotalGenerated = db.DuesInstallments.Sum(x => x.Amount);
        ViewBag.TotalCollections = db.Collections.Sum(x => x.Amount);
        ViewBag.BillingGroups = db.BillingGroups.Count(x => x.Active);
        ViewBag.ActiveUnits = db.Units.Count(x => x.Active);
        ViewBag.CombinedUnits = db.Units.Count(x => x.Active && x.IsCombined);
        ViewBag.OpenInstallments = db.DuesInstallments.Count(x => x.RemainingAmount > 0);
        ViewBag.PaidInstallments = db.DuesInstallments.Count(x => x.RemainingAmount <= 0);
        ViewBag.LedgerIncome = db.LedgerTransactions
            .Where(x => x.IncomeExpenseCategory != null && x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gelir)
            .Sum(x => x.Amount);
        ViewBag.LedgerExpense = db.LedgerTransactions
            .Where(x => x.IncomeExpenseCategory != null && x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gider)
            .Sum(x => x.Amount);
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
