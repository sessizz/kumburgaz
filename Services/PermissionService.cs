using System.Security.Claims;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Kumburgaz.Web.Services;

// Rol yetki matrisini okur ve önbelleğe alır; controller filtreleri ve menü görünürlüğü buradan sorgular.
public class PermissionService(ApplicationDbContext db, IMemoryCache cache)
{
    private const string CacheKey = "role-permissions-map";

    // (RoleName, Module) -> (CanView, CanWrite). Tekil kaynak; kayıtta Invalidate ile tazelenir.
    private Dictionary<(string Role, string Module), (bool View, bool Write)> LoadMap()
    {
        return cache.GetOrCreate(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
            return db.RolePermissions
                .AsNoTracking()
                .ToList()
                .ToDictionary(
                    x => (x.RoleName, x.Module),
                    x => (x.CanView, x.CanWrite));
        })!;
    }

    public void Invalidate() => cache.Remove(CacheKey);

    // needWrite=false ise görüntüleme yeter; yazma yetkisi görüntülemeyi de kapsar.
    public bool HasAccess(ClaimsPrincipal user, string module, bool needWrite)
    {
        // Sistem yöneticisi her zaman tam yetkili — matristen bağımsız, kilitlenme önlenir.
        if (user.IsInRole(AppRoles.SistemYonetici))
        {
            return true;
        }

        var map = LoadMap();
        foreach (var role in user.FindAll(ClaimTypes.Role).Select(c => c.Value))
        {
            if (map.TryGetValue((role, module), out var p))
            {
                if (needWrite ? p.Write : (p.View || p.Write))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool CanView(ClaimsPrincipal user, string module) => HasAccess(user, module, needWrite: false);

    public bool CanWrite(ClaimsPrincipal user, string module) => HasAccess(user, module, needWrite: true);
}
