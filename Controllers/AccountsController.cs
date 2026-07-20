using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[ModuleAuthorize(AppModules.Hesaplar)]
public class AccountsController(
    ApplicationDbContext db,
    UnitLedgerService unitLedgerService,
    ResidentAccountService residentAccountService,
    IDuesLedgerRowService ledgerService) : Controller
{
    private static string UnitLabel(Unit unit)
        => unit.Block is null ? unit.UnitNo : $"{unit.Block.Name}-{unit.UnitNo}";
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

        // Sıralama veritabanı harmanlamasıyla (collation) değil, Türkçe alfabeye göre yapılsın diye
        // önce çekilip C# tarafında sıralanıyor (Ç, Ğ, İ, Ö, Ş, Ü harfleri veritabanı harmanlamasında
        // sona düşebiliyor).
        var turkishComparer = StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), ignoreCase: false);
        var accounts = (await query.ToListAsync())
            .OrderBy(x => x.AccountType)
            .ThenBy(x => x.Name, turkishComparer)
            .ToList();

        var unitIds = accounts
            .SelectMany(x => x.UnitAccounts)
            .Where(x => x.Active)
            .Select(x => x.UnitId)
            .Distinct()
            .ToList();
        var summaries = await unitLedgerService.BuildSummariesAsync(unitIds);
        var netBalanceByAccountId = accounts.ToDictionary(
            x => x.Id,
            x => x.UnitAccounts
                .Where(ua => ua.Active)
                .Select(ua => ua.UnitId)
                .Distinct()
                .Sum(unitId => summaries.GetValueOrDefault(unitId)?.NetBalance ?? 0m));

        return View(new AccountIndexViewModel
        {
            Accounts = accounts,
            NetBalanceByAccountId = netBalanceByAccountId
        });
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? term)
    {
        term = term?.Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            return Json(Array.Empty<object>());
        }

        var normalizedTerm = term.ToLower();
        var accounts = await db.Accounts
            .AsNoTracking()
            .Include(x => x.UnitAccounts)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.UnitAccounts)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.CombinedUnitMembers)
            .Where(x => x.Name.ToLower().Contains(normalizedTerm)
                || (x.Phone != null && x.Phone.ToLower().Contains(normalizedTerm))
                || (x.Email != null && x.Email.ToLower().Contains(normalizedTerm))
                || x.UnitAccounts.Any(unitAccount =>
                    unitAccount.Active &&
                    unitAccount.Unit != null &&
                    (unitAccount.Unit.UnitNo.ToLower().Contains(normalizedTerm) ||
                     (unitAccount.Unit.OwnerName != null && unitAccount.Unit.OwnerName.ToLower().Contains(normalizedTerm)) ||
                     (unitAccount.Unit.Block != null && unitAccount.Unit.Block.Name.ToLower().Contains(normalizedTerm)))))
            .OrderBy(x => x.Name.ToLower() == normalizedTerm ? 0 : 1)
            .ThenBy(x => x.Name)
            .Take(12)
            .ToListAsync();

        var results = accounts.Select(account =>
        {
            var combinedComponentIds = account.UnitAccounts
                .Where(x => x.Active && x.Unit?.IsCombined == true)
                .SelectMany(x => x.Unit!.CombinedUnitMembers.Select(member => member.ComponentUnitId))
                .ToHashSet();

            var unitLinks = account.UnitAccounts
                .Where(x => x.Active && x.Unit?.Block is not null)
                .Where(x => x.Unit!.IsCombined || !combinedComponentIds.Contains(x.Unit.Id))
                .OrderBy(x => x.Unit!.Block!.Name)
                .ThenBy(x => x.Unit!.UnitNo)
                .Select(x =>
                {
                    var owner = string.IsNullOrWhiteSpace(x.Unit!.OwnerName) ? "" : $" / Malik: {x.Unit.OwnerName}";
                    return $"{x.Unit.Block!.Name}-{x.Unit.UnitNo} ({AccountDisplayHelper.RoleLabel(x.Role)}){owner}";
                })
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

        var effectiveRemaining = OpeningBalanceCreditHelper.BuildEffectiveRemainingMap(openInstallments);
        var openRows = openInstallments
            .Select(x => new AccountOpenInstallmentViewModel
            {
                Id = x.Id,
                UnitId = x.UnitId,
                Period = x.Period,
                UnitDisplay = x.Unit is not null ? UnitDisplayHelper.Display(x.Unit) : BillingGroupDisplayHelper.UnitDisplay(x.BillingGroup),
                DuesTypeName = x.BillingGroup?.DuesType?.Name ?? "Aidat",
                DueDate = x.DueDate,
                RemainingAmount = effectiveRemaining.GetValueOrDefault(x.Id, x.RemainingAmount)
            })
            .Where(x => x.RemainingAmount > 0)
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.Period)
            .ThenBy(x => x.UnitDisplay)
            .ToList();

        var openingDebtRemaining = await ledgerService.GetOpeningDebtRemainingByUnitAsync();
        var openingDebtRows = account.UnitAccounts
            .Where(x => x.Active && x.Unit is { Active: true, OpeningBalance: < 0m })
            .Select(x => new AccountOpenInstallmentViewModel
            {
                UnitId = x.UnitId,
                Period = "Devir",
                UnitDisplay = UnitDisplayHelper.Display(x.Unit!),
                DuesTypeName = "Devir Bakiyesi",
                DueDate = x.Unit!.OpeningBalanceDate ?? DateTime.Today,
                // Devir borcuna fiilen tahsis edilmis (kalici) tutar dusulur.
                RemainingAmount = openingDebtRemaining.GetValueOrDefault(x.UnitId, -x.Unit.OpeningBalance),
                IsOpeningBalance = true
            })
            .Where(x => x.RemainingAmount > 0);

        openRows = openingDebtRows
            .Concat(openRows)
            .OrderBy(x => x.DueDate)
            .ThenBy(x => x.Period)
            .ThenBy(x => x.UnitDisplay)
            .ToList();

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

        var openingRows = account.UnitAccounts
            .Where(x => x.Active && x.Unit is { OpeningBalance: > 0m })
            .Select(x => new AccountCollectionRowViewModel
            {
                Date = x.Unit!.OpeningBalanceDate ?? DateTime.Today,
                Description = $"{UnitDisplayHelper.Display(x.Unit)} - Devir Bakiyesi",
                AccountName = "Devir",
                Amount = x.Unit.OpeningBalance,
                IsOpeningBalance = true
            });

        var collectionRows = recentAllocations
            .Select(x => new AccountCollectionRowViewModel
            {
                Date = x.Collection?.Date ?? DateTime.MinValue,
                Description = $"{x.DuesInstallment?.Period} - {x.DuesInstallment?.BillingGroup?.DuesType?.Name ?? "Aidat"}",
                AccountName = x.Collection?.CashBox?.Name ?? x.Collection?.BankAccount?.Name ?? "—",
                Amount = x.AppliedAmount
            });

        var unitIds = account.UnitAccounts
            .Where(x => x.Active && x.Unit is { Active: true })
            .Select(x => x.UnitId)
            .Distinct()
            .ToList();
        var summaries = await unitLedgerService.BuildSummariesAsync(unitIds);
        var summary = new UnitLedgerSummary
        {
            TotalAccrual = summaries.Values.Sum(x => x.TotalAccrual),
            TotalCollections = summaries.Values.Sum(x => x.TotalCollections),
            OpeningCredit = summaries.Values.Sum(x => x.OpeningCredit),
            OpeningDebt = summaries.Values.Sum(x => x.OpeningDebt),
            NetBalance = summaries.Values.Sum(x => x.NetBalance)
        };

        return View(new AccountDetailViewModel
        {
            Account = account,
            OpenInstallments = openRows,
            Summary = summary,
            RecentCollections = openingRows
                .Concat(collectionRows)
                .OrderByDescending(x => x.Date)
                .Take(20)
                .ToList()
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
        // Malik/Kiracı ise mobil giriş + PIN otomatik oluştur.
        await residentAccountService.EnsureLoginAsync(account);
        TempData["ActionSuccess"] = "Hesap oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var account = await db.Accounts
            .Include(x => x.UnitAccounts).ThenInclude(x => x.Unit).ThenInclude(x => x!.Block)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (account is null)
        {
            return NotFound();
        }

        return View(await BuildEditPageAsync(account));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(AccountFormViewModel model)
    {
        if (model.Id is null)
        {
            return NotFound();
        }

        var account = await db.Accounts
            .Include(x => x.UnitAccounts).ThenInclude(x => x.Unit).ThenInclude(x => x!.Block)
            .FirstOrDefaultAsync(x => x.Id == model.Id.Value);
        if (account is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(await BuildEditPageAsync(account, model));
        }

        ApplyModel(account, model);
        await db.SaveChangesAsync();
        // Malik/Kiracı olduysa giriş oluştur (idempotent).
        await residentAccountService.EnsureLoginAsync(account);
        TempData["ActionSuccess"] = "Hesap güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    // Hesaba, sahibi olmadığı bir daireyi ek erişim olarak tanımlar (yönetici).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddUnitAccess(int accountId, int unitId)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(x => x.Id == accountId);
        var unitExists = await db.Units.AnyAsync(x => x.Id == unitId);
        if (account is null || !unitExists)
        {
            TempData["ActionError"] = "Hesap veya daire bulunamadı.";
            return RedirectToAction(nameof(Edit), new { id = accountId });
        }

        var alreadyGranted = await db.AccountUnitAccesses.AnyAsync(x => x.AccountId == accountId && x.UnitId == unitId);
        var alreadyOwned = await db.UnitAccounts.AnyAsync(x => x.AccountId == accountId && x.UnitId == unitId && x.Active);
        if (!alreadyGranted && !alreadyOwned)
        {
            db.AccountUnitAccesses.Add(new AccountUnitAccess
            {
                AccountId = accountId,
                UnitId = unitId,
                CreatedByUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                CreatedByUserName = User.FindFirst(ApplicationUserClaimsPrincipalFactory.DisplayNameClaimType)?.Value
            });
            await db.SaveChangesAsync();
            TempData["ActionSuccess"] = "Daire erişimi eklendi.";
        }

        return RedirectToAction(nameof(Edit), new { id = accountId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveUnitAccess(int accountId, int unitId)
    {
        var grant = await db.AccountUnitAccesses.FirstOrDefaultAsync(x => x.AccountId == accountId && x.UnitId == unitId);
        if (grant is not null)
        {
            db.AccountUnitAccesses.Remove(grant);
            await db.SaveChangesAsync();
            TempData["ActionSuccess"] = "Daire erişimi kaldırıldı.";
        }

        return RedirectToAction(nameof(Edit), new { id = accountId });
    }

    // Mobil giriş şifresini görme/sıfırlama yalnızca Sistem Yöneticisi.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPolicies.SystemAdmin)]
    public async Task<IActionResult> ResetMobilePassword(int accountId)
    {
        var pin = await residentAccountService.ResetPasswordAsync(accountId);
        TempData[pin is null ? "ActionError" : "ActionSuccess"] = pin is null
            ? "Şifre üretilemedi (hesap Malik/Kiracı olmalı)."
            : $"Yeni şifre: {pin}";
        return RedirectToAction(nameof(Edit), new { id = accountId });
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

    private async Task<AccountEditPageViewModel> BuildEditPageAsync(Account account, AccountFormViewModel? form = null)
    {
        var isResident = account.AccountType is AccountType.Owner or AccountType.Tenant;

        var owned = account.UnitAccounts
            .Where(x => x.Active && x.Unit is not null)
            .Select(x => new AccountUnitAccessRow
            {
                UnitId = x.UnitId,
                Display = UnitLabel(x.Unit!),
                Role = AccountDisplayHelper.RoleLabel(x.Role),
                IsOwned = true
            })
            .OrderBy(x => x.Display)
            .ToList();
        var ownedIds = owned.Select(x => x.UnitId).ToHashSet();

        var grants = await db.AccountUnitAccesses.AsNoTracking()
            .Where(x => x.AccountId == account.Id)
            .Include(x => x.Unit).ThenInclude(x => x!.Block)
            .ToListAsync();
        var granted = grants
            .Where(x => x.Unit is not null)
            .Select(x => new AccountUnitAccessRow { UnitId = x.UnitId, Display = UnitLabel(x.Unit!), IsOwned = false })
            .OrderBy(x => x.Display)
            .ToList();

        var exclude = ownedIds.Concat(granted.Select(x => x.UnitId)).ToHashSet();
        var unitOptions = await db.Units.AsNoTracking()
            .Where(x => x.Active && !exclude.Contains(x.Id))
            .Include(x => x.Block)
            .OrderBy(x => x.Block!.Name).ThenBy(x => x.UnitNo)
            .Select(x => new SelectListItem($"{x.Block!.Name}-{x.UnitNo}", x.Id.ToString()))
            .ToListAsync();

        var hasLogin = isResident && await db.Users.AnyAsync(x => x.AccountId == account.Id);

        return new AccountEditPageViewModel
        {
            Form = form ?? ToFormModel(account),
            IsResidentAccount = isResident,
            CanManageLogin = User.IsInRole(AppRoles.SistemYonetici),
            HasLogin = hasLogin,
            UserName = account.Id.ToString(),
            MobilePassword = account.MobilePassword,
            OwnedUnits = owned,
            GrantedUnits = granted,
            UnitOptions = unitOptions
        };
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
