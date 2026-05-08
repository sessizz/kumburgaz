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
        var todayUtc = DateTime.UtcNow.Date;

        // Vadesi geçmiş taksitler (DueDate < bugün) ve henüz vadesi gelmemiş taksitler ayrı ayrı
        var overdueRemaining = db.DuesInstallments
            .Where(x => x.RemainingAmount > 0 && x.DueDate < todayUtc)
            .Sum(x => (decimal?)x.RemainingAmount) ?? 0m;
        var pendingRemaining = db.DuesInstallments
            .Where(x => x.RemainingAmount > 0 && x.DueDate >= todayUtc)
            .Sum(x => (decimal?)x.RemainingAmount) ?? 0m;

        // Devir alacakları (pozitif) → Tahsilat'a eklenir
        // Devir borçları  (negatif) → Geciken'e eklenir
        var openingCredit = db.Units.Where(x => x.Active && x.OpeningBalance > 0).Sum(x => (decimal?)x.OpeningBalance) ?? 0m;
        var openingDebt   = db.Units.Where(x => x.Active && x.OpeningBalance < 0).Sum(x => (decimal?)(-x.OpeningBalance)) ?? 0m;

        var actualCollections = db.Collections.Sum(x => (decimal?)x.Amount) ?? 0m;

        ViewBag.TotalDebt        = overdueRemaining + openingDebt;   // Geciken
        ViewBag.PendingDebt      = pendingRemaining;                  // Vadesi gelmemiş
        ViewBag.TotalCollections = actualCollections + openingCredit; // Tahsilat (devir alacak dahil)
        ViewBag.OpeningCredit    = openingCredit;
        ViewBag.OpeningDebt      = openingDebt;
        ViewBag.OpeningBalanceNet = openingCredit - openingDebt;      // (bilgi notu için)
        ViewBag.TotalGenerated = db.DuesInstallments.Sum(x => x.Amount);
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
