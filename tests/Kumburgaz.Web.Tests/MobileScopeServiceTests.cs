using System.Security.Claims;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class MobileScopeServiceTests
{
    [Fact]
    public void IsResident_true_for_sakin_role()
    {
        var user = ResidentPrincipal(accountId: 1);
        Assert.True(new MobileScopeService(CreateDb()).IsResident(user));
    }

    [Fact]
    public void IsResident_false_for_staff()
    {
        var user = StaffPrincipal();
        Assert.False(new MobileScopeService(CreateDb()).IsResident(user));
    }

    [Fact]
    public async Task GetAllowedUnitIdsAsync_returns_null_for_staff_unrestricted()
    {
        await using var db = CreateDb();
        var allowed = await new MobileScopeService(db).GetAllowedUnitIdsAsync(StaffPrincipal());
        Assert.Null(allowed);
    }

    [Fact]
    public async Task GetAllowedUnitIdsAsync_unions_owned_and_granted_units_deduplicated()
    {
        await using var db = CreateDb();
        var site = new Site { Name = "Test Site" };
        var block = new Block { Site = site, Name = "A Blok" };
        var unit1 = new Unit { Block = block, UnitNo = "1", Active = true };
        var unit2 = new Unit { Block = block, UnitNo = "2", Active = true };
        var unit3 = new Unit { Block = block, UnitNo = "3", Active = true };
        var account = new Account { Name = "Test Hesap", AccountType = AccountType.Owner, Active = true };
        db.AddRange(site, block, unit1, unit2, unit3, account);
        await db.SaveChangesAsync();

        // Account owns unit1 and unit2, and separately has AccountUnitAccess granted to unit2 (overlap) and unit3.
        // AccountUnitAccess'in query filter'i (!Account.IsDeleted && !Unit.IsDeleted) gercek Account/Unit satiri gerektirir.
        db.UnitAccounts.Add(new UnitAccount { UnitId = unit1.Id, AccountId = account.Id, Role = UnitAccountRole.Owner, Active = true });
        db.UnitAccounts.Add(new UnitAccount { UnitId = unit2.Id, AccountId = account.Id, Role = UnitAccountRole.Owner, Active = true });
        db.AccountUnitAccesses.Add(new AccountUnitAccess { UnitId = unit2.Id, AccountId = account.Id });
        db.AccountUnitAccesses.Add(new AccountUnitAccess { UnitId = unit3.Id, AccountId = account.Id });
        await db.SaveChangesAsync();

        var allowed = await new MobileScopeService(db).GetAllowedUnitIdsAsync(ResidentPrincipal(accountId: account.Id));

        Assert.NotNull(allowed);
        Assert.Equal([unit1.Id, unit2.Id, unit3.Id], allowed!.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task GetAllowedUnitIdsAsync_ignores_inactive_ownership()
    {
        await using var db = CreateDb();
        var site = new Site { Name = "Test Site" };
        var block = new Block { Site = site, Name = "A Blok" };
        var unit = new Unit { Block = block, UnitNo = "1", Active = true };
        db.AddRange(site, block, unit);
        await db.SaveChangesAsync();

        db.UnitAccounts.Add(new UnitAccount { UnitId = unit.Id, AccountId = 3, Role = UnitAccountRole.Owner, Active = false });
        await db.SaveChangesAsync();

        var allowed = await new MobileScopeService(db).GetAllowedUnitIdsAsync(ResidentPrincipal(accountId: 3));
        Assert.Empty(allowed!);
    }

    [Fact]
    public async Task GetAllowedUnitIdsAsync_returns_empty_when_resident_has_no_account_claim()
    {
        await using var db = CreateDb();
        var allowed = await new MobileScopeService(db).GetAllowedUnitIdsAsync(ResidentPrincipal(accountId: null));
        Assert.NotNull(allowed);
        Assert.Empty(allowed!);
    }

    [Fact]
    public async Task CanAccessUnitAsync_true_for_staff_on_any_unit()
    {
        await using var db = CreateDb();
        Assert.True(await new MobileScopeService(db).CanAccessUnitAsync(StaffPrincipal(), unitId: 999));
    }

    [Fact]
    public async Task CanAccessUnitAsync_false_for_resident_outside_scope()
    {
        await using var db = CreateDb();
        var site = new Site { Name = "Test Site" };
        var block = new Block { Site = site, Name = "A Blok" };
        var ownedUnit = new Unit { Block = block, UnitNo = "1", Active = true };
        var otherUnit = new Unit { Block = block, UnitNo = "2", Active = true };
        db.AddRange(site, block, ownedUnit, otherUnit);
        await db.SaveChangesAsync();
        db.UnitAccounts.Add(new UnitAccount { UnitId = ownedUnit.Id, AccountId = 5, Role = UnitAccountRole.Owner, Active = true });
        await db.SaveChangesAsync();

        var scope = new MobileScopeService(db);
        var resident = ResidentPrincipal(accountId: 5);

        Assert.True(await scope.CanAccessUnitAsync(resident, ownedUnit.Id));
        Assert.False(await scope.CanAccessUnitAsync(resident, otherUnit.Id));
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ClaimsPrincipal StaffPrincipal()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, AppRoles.SistemYonetici)], "Test");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal ResidentPrincipal(int? accountId)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, AppRoles.Sakin) };
        if (accountId.HasValue)
        {
            claims.Add(new Claim(ApplicationUserClaimsPrincipalFactory.AccountIdClaimType, accountId.Value.ToString()));
        }
        var identity = new ClaimsIdentity(claims, "Test");
        return new ClaimsPrincipal(identity);
    }
}
