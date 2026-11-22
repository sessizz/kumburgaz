using System.Security.Claims;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class PermissionServiceTests
{
    // Çerez oturumu: roller ClaimTypes.Role ile taşınır.
    [Fact]
    public void Cookie_principal_uses_role_matrix()
    {
        using var db = CreateDb();
        var service = CreateService(db);
        var user = Principal(new ClaimsIdentity([new Claim(ClaimTypes.Role, AppRoles.MuhasebeGorevli)], "Identity.Application"));

        AssertMuhasebeGorevliAccess(service, user);
    }

    // OpenIddict token'ı: roller kimliğin RoleClaimType'ı olan "role" ile taşınır.
    [Fact]
    public void Token_principal_uses_role_matrix_via_role_claim_type()
    {
        using var db = CreateDb();
        var service = CreateService(db);
        var user = Principal(new ClaimsIdentity([new Claim("role", AppRoles.MuhasebeGorevli)], "Bearer", "name", "role"));

        AssertMuhasebeGorevliAccess(service, user);
    }

    [Fact]
    public void Token_principal_with_system_admin_role_has_full_access()
    {
        using var db = CreateDb();
        var service = CreateService(db);
        var user = Principal(new ClaimsIdentity([new Claim("role", AppRoles.SistemYonetici)], "Bearer", "name", "role"));

        Assert.True(service.CanWrite(user, AppModules.Belgeler));
    }

    // "role" claim'i yalnız kimliğin RoleClaimType'ı ise rol sayılır; çerez kullanıcısının davranışı değişmez.
    [Fact]
    public void Role_claim_is_ignored_when_it_is_not_the_identity_role_claim_type()
    {
        using var db = CreateDb();
        var service = CreateService(db);
        var user = Principal(new ClaimsIdentity([new Claim("role", AppRoles.MuhasebeGorevli)], "Identity.Application"));

        Assert.False(service.CanView(user, AppModules.Muhasebe));
    }

    private static void AssertMuhasebeGorevliAccess(PermissionService service, ClaimsPrincipal user)
    {
        Assert.True(service.CanView(user, AppModules.Muhasebe));
        Assert.True(service.CanWrite(user, AppModules.Muhasebe));
        Assert.True(service.CanView(user, AppModules.Raporlar));
        Assert.False(service.CanWrite(user, AppModules.Raporlar));
        Assert.False(service.CanView(user, AppModules.Daireler));
    }

    private static ClaimsPrincipal Principal(ClaimsIdentity identity) => new(identity);

    private static PermissionService CreateService(ApplicationDbContext db)
    {
        db.RolePermissions.AddRange(
            new RolePermission { RoleName = AppRoles.MuhasebeGorevli, Module = AppModules.Muhasebe, CanView = true, CanWrite = true },
            new RolePermission { RoleName = AppRoles.MuhasebeGorevli, Module = AppModules.Raporlar, CanView = true, CanWrite = false });
        db.SaveChanges();
        return new PermissionService(db, new MemoryCache(new MemoryCacheOptions()));
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
