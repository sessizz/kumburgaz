using System.Security.Claims;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

/// <summary>
/// Sakin (daire sahibi/kiracı) kullanıcılarının hangi dairelere erişebileceğini çözer.
/// İzinli daireler = sahiplik (aktif UnitAccount) BİRLEŞİM ek tanımlı erişimler (AccountUnitAccess).
/// Yönetici/personel için kısıt yoktur (null döner).
/// </summary>
public sealed class MobileScopeService(ApplicationDbContext db)
{
    public int? GetAccountId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst(ApplicationUserClaimsPrincipalFactory.AccountIdClaimType)?.Value;
        return int.TryParse(raw, out var id) ? id : null;
    }

    public bool IsResident(ClaimsPrincipal user) => user.IsInRole(AppRoles.Sakin);

    /// <summary>Sakin ise izinli daire Id listesi; değilse null (kısıtsız).</summary>
    public async Task<IReadOnlyList<int>?> GetAllowedUnitIdsAsync(ClaimsPrincipal user)
    {
        if (!IsResident(user))
        {
            return null;
        }

        var accountId = GetAccountId(user);
        if (accountId is null)
        {
            return Array.Empty<int>();
        }

        var owned = await db.UnitAccounts.AsNoTracking()
            .Where(x => x.AccountId == accountId.Value && x.Active)
            .Select(x => x.UnitId)
            .ToListAsync();

        var granted = await db.AccountUnitAccesses.AsNoTracking()
            .Where(x => x.AccountId == accountId.Value)
            .Select(x => x.UnitId)
            .ToListAsync();

        return owned.Concat(granted).Distinct().ToList();
    }

    /// <summary>Kullanıcı bu daireyi görebilir mi? Yönetici her zaman true.</summary>
    public async Task<bool> CanAccessUnitAsync(ClaimsPrincipal user, int unitId)
    {
        var allowed = await GetAllowedUnitIdsAsync(user);
        return allowed is null || allowed.Contains(unitId);
    }
}
