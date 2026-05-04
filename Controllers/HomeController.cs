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

        var collections = db.Collections
            .Select(x => new { x.CashBoxId, x.BankAccountId, x.Amount })
            .ToList();
        var expenses = db.LedgerTransactions
            .Where(x => x.IncomeExpenseCategory != null && x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gider)
            .Select(x => new { x.CashBoxId, x.BankAccountId, x.Amount })
            .ToList();
        var cashBoxes = db.CashBoxes.Where(x => x.Active).OrderBy(x => x.Name).ToList();
        var bankAccounts = db.BankAccounts.Where(x => x.Active).OrderBy(x => x.Name).ThenBy(x => x.Branch).ToList();

        var cashBankItems = new List<CashBankListItemViewModel>();
        cashBankItems.AddRange(cashBoxes.Select(x => new CashBankListItemViewModel
        {
            Type = "cash",
            Name = x.Name,
            Balance = x.OpeningBalance
                + collections.Where(c => c.CashBoxId == x.Id).Sum(c => c.Amount)
                - expenses.Where(e => e.CashBoxId == x.Id).Sum(e => e.Amount)
        }));
        cashBankItems.AddRange(bankAccounts.Select(x => new CashBankListItemViewModel
        {
            Type = "bank",
            Name = string.IsNullOrWhiteSpace(x.Branch) ? x.Name : $"{x.Name} - {x.Branch}",
            Balance = x.OpeningBalance
                + collections.Where(c => c.BankAccountId == x.Id).Sum(c => c.Amount)
                - expenses.Where(e => e.BankAccountId == x.Id).Sum(e => e.Amount)
        }));
        ViewBag.CashBankItems = cashBankItems;

        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
