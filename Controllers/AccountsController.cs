using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize]
public class AccountsController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(string? q = null, AccountType? type = null)
    {
        var query = db.Accounts
            .AsNoTracking()
            .Include(x => x.UnitAccounts)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .AsQueryable();

        var term = q?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(x =>
                x.Name.Contains(term) ||
                (x.Phone != null && x.Phone.Contains(term)) ||
                (x.Email != null && x.Email.Contains(term)));
        }

        if (type.HasValue)
        {
            query = query.Where(x => x.AccountType == type.Value);
        }

        ViewBag.Query = term ?? string.Empty;
        ViewBag.Type = type;

        var accounts = await query
            .OrderBy(x => x.AccountType)
            .ThenBy(x => x.Name)
            .ToListAsync();

        return View(accounts);
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? term)
    {
        term = term?.Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            return Json(Array.Empty<object>());
        }

        var accounts = await db.Accounts
            .AsNoTracking()
            .Include(x => x.UnitAccounts)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Where(x => x.Name.Contains(term)
                || (x.Phone != null && x.Phone.Contains(term))
                || (x.Email != null && x.Email.Contains(term))
                || x.UnitAccounts.Any(unitAccount =>
                    unitAccount.Active &&
                    unitAccount.Unit != null &&
                    (unitAccount.Unit.UnitNo.Contains(term) ||
                     (unitAccount.Unit.Block != null && unitAccount.Unit.Block.Name.Contains(term)))))
            .OrderBy(x => x.Name == term ? 0 : 1)
            .ThenBy(x => x.Name)
            .Take(12)
            .ToListAsync();

        var results = accounts.Select(account =>
        {
            var unitLinks = account.UnitAccounts
                .Where(x => x.Active && x.Unit?.Block is not null)
                .OrderBy(x => x.Unit!.Block!.Name)
                .ThenBy(x => x.Unit!.UnitNo)
                .Select(x => $"{x.Unit!.Block!.Name}-{x.Unit.UnitNo} ({AccountDisplayHelper.RoleLabel(x.Role)})")
                .ToList();

            var subtitleParts = new List<string> { AccountDisplayHelper.TypeLabel(account.AccountType) };
            if (unitLinks.Count > 0)
            {
                subtitleParts.Add(string.Join(", ", unitLinks));
            }

            return new
            {
                id = account.Id,
                label = account.Name,
                subtitle = string.Join(" • ", subtitleParts),
                active = account.Active,
                url = Url.Action(nameof(Detail), "Accounts", new { id = account.Id })
            };
        });

        return Json(results);
    }

    public async Task<IActionResult> Detail(int id)
    {
        var account = await db.Accounts
            .AsNoTracking()
            .Include(x => x.UnitAccounts)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (account is null)
        {
            return NotFound();
        }

        var openInstallments = await db.DuesInstallments
            .AsNoTracking()
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.DuesType)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.CombinedUnitMembers)
            .ThenInclude(x => x.ComponentUnit)
            .ThenInclude(x => x!.Block)
            .Where(x => x.ResponsibleAccountId == id && x.RemainingAmount > 0)
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.Period)
            .ToListAsync();

        var recentAllocations = await db.CollectionAllocations
            .AsNoTracking()
            .Include(x => x.Collection)
            .ThenInclude(x => x!.CashBox)
            .Include(x => x.Collection)
            .ThenInclude(x => x!.BankAccount)
            .Include(x => x.DuesInstallment)
            .ThenInclude(x => x!.BillingGroup)
            .ThenInclude(x => x!.DuesType)
            .Where(x => x.DuesInstallment != null && x.DuesInstallment.ResponsibleAccountId == id)
            .OrderByDescending(x => x.Collection!.Date)
            .ThenByDescending(x => x.Id)
            .Take(20)
            .ToListAsync();

        return View(new AccountDetailViewModel
        {
            Account = account,
            OpenInstallments = openInstallments,
            RecentAllocations = recentAllocations
        });
    }

    public IActionResult Create()
    {
        return View(new AccountFormViewModel { Active = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AccountFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var account = new Account();
        ApplyModel(account, model);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Hesap oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var account = await db.Accounts.FindAsync(id);
        if (account is null)
        {
            return NotFound();
        }

        return View(ToFormModel(account));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AccountFormViewModel model)
    {
        if (model.Id is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var account = await db.Accounts.FindAsync(model.Id.Value);
        if (account is null)
        {
            return NotFound();
        }

        ApplyModel(account, model);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Hesap güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var account = await db.Accounts
            .Include(x => x.UnitAccounts)
            .Include(x => x.DuesInstallments)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (account is null)
        {
            TempData["ActionError"] = "Hesap bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        if (account.UnitAccounts.Count > 0 || account.DuesInstallments.Count > 0)
        {
            account.Active = false;
            await db.SaveChangesAsync();
            TempData["ActionSuccess"] = "Hesap bağlı kayıt içerdiği için pasife alındı.";
            return RedirectToAction(nameof(Index));
        }

        db.Accounts.Remove(account);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Hesap silindi.";
        return RedirectToAction(nameof(Index));
    }

    private static AccountFormViewModel ToFormModel(Account account)
    {
        return new AccountFormViewModel
        {
            Id = account.Id,
            Name = account.Name,
            AccountType = account.AccountType,
            Phone = account.Phone,
            Email = account.Email,
            Note = account.Note,
            Active = account.Active
        };
    }

    private static void ApplyModel(Account account, AccountFormViewModel model)
    {
        account.Name = model.Name.Trim();
        account.AccountType = model.AccountType;
        account.Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
        account.Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim();
        account.Note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim();
        account.Active = model.Active;
    }
}
