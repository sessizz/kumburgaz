using System.Security.Cryptography;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

/// <summary>
/// Malik/Kiracı hesapları için mobil giriş (Sakin) yönetimi.
/// Kullanıcı adı = hesabın Id'si. Şifre 5 haneli PIN; hem Identity hash'i hem de
/// hesabın MobilePassword alanında düz metin tutulur (yönetici görebilsin diye).
/// </summary>
public sealed class ResidentAccountService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager)
{
    public static string GeneratePin() => RandomNumberGenerator.GetInt32(10000, 100000).ToString();

    private static bool IsResidentAccount(Account account)
        => account.AccountType is AccountType.Owner or AccountType.Tenant;

    /// <summary>
    /// Hesap için giriş yoksa oluşturur (Malik/Kiracı değilse dokunmaz). Idempotent.
    /// Yeni PIN üretir, Identity kullanıcısı açar, Sakin rolü ve hesap bağlantısı atar,
    /// PIN'i hesapta saklar.
    /// </summary>
    public async Task EnsureLoginAsync(Account account)
    {
        if (!IsResidentAccount(account))
        {
            return;
        }

        var existing = await userManager.Users.FirstOrDefaultAsync(x => x.AccountId == account.Id);
        if (existing is not null)
        {
            return;
        }

        var pin = GeneratePin();
        var user = new ApplicationUser
        {
            UserName = account.Id.ToString(),
            Email = $"acc-{account.Id}@kumburgaz.local",
            EmailConfirmed = true,
            FullName = account.Name,
            AccountId = account.Id
        };

        var result = await userManager.CreateAsync(user, pin);
        if (!result.Succeeded)
        {
            return;
        }

        await userManager.AddToRoleAsync(user, AppRoles.Sakin);

        var tracked = await db.Accounts.FirstOrDefaultAsync(x => x.Id == account.Id);
        if (tracked is not null)
        {
            tracked.MobilePassword = pin;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Yöneticinin bir hesabın şifresini görmesi/sıfırlaması. newPin verilmezse yeni üretir.
    /// Giriş yoksa oluşturur. Döndürülen değer yeni/mevcut PIN'dir.
    /// </summary>
    public async Task<string?> ResetPasswordAsync(int accountId, string? newPin = null)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(x => x.Id == accountId);
        if (account is null || !IsResidentAccount(account))
        {
            return null;
        }

        var user = await userManager.Users.FirstOrDefaultAsync(x => x.AccountId == accountId);
        if (user is null)
        {
            await EnsureLoginAsync(account);
            var created = await db.Accounts.FirstOrDefaultAsync(x => x.Id == accountId);
            return created?.MobilePassword;
        }

        var pin = string.IsNullOrWhiteSpace(newPin) ? GeneratePin() : newPin.Trim();
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, pin);
        if (!result.Succeeded)
        {
            return null;
        }

        account.MobilePassword = pin;
        await db.SaveChangesAsync();
        return pin;
    }

    /// <summary>Sakinin kendi PIN'ini değiştirmesi. Düz metin de güncellenir.</summary>
    public async Task<IdentityResult> ChangeOwnPasswordAsync(ApplicationUser user, string currentPin, string newPin)
    {
        var result = await userManager.ChangePasswordAsync(user, currentPin, newPin);
        if (result.Succeeded && user.AccountId.HasValue)
        {
            var account = await db.Accounts.FirstOrDefaultAsync(x => x.Id == user.AccountId.Value);
            if (account is not null)
            {
                account.MobilePassword = newPin;
                await db.SaveChangesAsync();
            }
        }

        return result;
    }

    /// <summary>Kullanıcı Giriş Bilgileri raporu için tüm Malik/Kiracı hesaplarını döndürür.</summary>
    public async Task<List<ResidentCredentialRow>> GetCredentialsAsync()
    {
        var accounts = await db.Accounts.AsNoTracking()
            .Where(x => x.AccountType == AccountType.Owner || x.AccountType == AccountType.Tenant)
            .OrderBy(x => x.AccountType)
            .ThenBy(x => x.Name)
            .ToListAsync();

        var accountIds = accounts.Select(x => x.Id).ToList();

        var linkedUserAccountIds = await userManager.Users.AsNoTracking()
            .Where(x => x.AccountId != null && accountIds.Contains(x.AccountId.Value))
            .Select(x => x.AccountId!.Value)
            .ToListAsync();
        var hasLogin = linkedUserAccountIds.ToHashSet();

        // Sahiplik (UnitAccount) + ek erişim (AccountUnitAccess) dairelerini derle.
        var ownedUnits = await db.UnitAccounts.AsNoTracking()
            .Where(x => x.Active && accountIds.Contains(x.AccountId))
            .Include(x => x.Unit).ThenInclude(x => x!.Block)
            .Select(x => new { x.AccountId, x.Unit })
            .ToListAsync();

        var grantedUnits = await db.AccountUnitAccesses.AsNoTracking()
            .Where(x => accountIds.Contains(x.AccountId))
            .Include(x => x.Unit).ThenInclude(x => x!.Block)
            .Select(x => new { x.AccountId, x.Unit })
            .ToListAsync();

        string Label(Unit? u) => u is null ? string.Empty : (u.Block is null ? u.UnitNo : $"{u.Block.Name}-{u.UnitNo}");

        var unitsByAccount = accountIds.ToDictionary(id => id, _ => new SortedSet<string>(StringComparer.CurrentCulture));
        foreach (var row in ownedUnits)
        {
            var label = Label(row.Unit);
            if (!string.IsNullOrEmpty(label)) unitsByAccount[row.AccountId].Add(label);
        }
        foreach (var row in grantedUnits)
        {
            var label = Label(row.Unit);
            if (!string.IsNullOrEmpty(label)) unitsByAccount[row.AccountId].Add(label);
        }

        return accounts.Select(x => new ResidentCredentialRow
        {
            AccountId = x.Id,
            AccountName = x.Name,
            AccountTypeLabel = AccountDisplayHelper.TypeLabel(x.AccountType),
            Phone = x.Phone,
            UserName = x.Id.ToString(),
            Password = x.MobilePassword,
            Units = string.Join(", ", unitsByAccount[x.Id]),
            HasLogin = hasLogin.Contains(x.Id)
        }).ToList();
    }
}
