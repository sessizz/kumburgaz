using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class ResidentAccountServiceTests
{
    [Fact]
    public async Task EnsureLoginAsync_creates_login_with_sakin_role_and_plaintext_pin_for_owner()
    {
        await using var db = CreateDb();
        await SeedSakinRoleAsync(db);
        var userManager = CreateUserManager(db);
        var account = await AddAccountAsync(db, AccountType.Owner);

        await new ResidentAccountService(db, userManager).EnsureLoginAsync(account);

        var user = await userManager.Users.SingleOrDefaultAsync(x => x.AccountId == account.Id);
        Assert.NotNull(user);
        Assert.Equal(account.Id.ToString(), user!.UserName);
        Assert.True(await userManager.IsInRoleAsync(user, AppRoles.Sakin));

        var updated = await db.Accounts.FindAsync(account.Id);
        Assert.False(string.IsNullOrEmpty(updated!.MobilePassword));
        Assert.Matches("^[0-9]{5}$", updated.MobilePassword!);
    }

    [Fact]
    public async Task EnsureLoginAsync_is_idempotent()
    {
        await using var db = CreateDb();
        await SeedSakinRoleAsync(db);
        var userManager = CreateUserManager(db);
        var account = await AddAccountAsync(db, AccountType.Tenant);
        var service = new ResidentAccountService(db, userManager);

        await service.EnsureLoginAsync(account);
        var firstPin = (await db.Accounts.FindAsync(account.Id))!.MobilePassword;
        await service.EnsureLoginAsync(account);

        var users = await userManager.Users.Where(x => x.AccountId == account.Id).ToListAsync();
        Assert.Single(users);
        Assert.Equal(firstPin, (await db.Accounts.FindAsync(account.Id))!.MobilePassword);
    }

    [Fact]
    public async Task EnsureLoginAsync_skips_non_resident_account_types()
    {
        await using var db = CreateDb();
        await SeedSakinRoleAsync(db);
        var userManager = CreateUserManager(db);
        var account = await AddAccountAsync(db, AccountType.Supplier);

        await new ResidentAccountService(db, userManager).EnsureLoginAsync(account);

        Assert.False(await userManager.Users.AnyAsync(x => x.AccountId == account.Id));
    }

    [Fact]
    public async Task ChangeOwnPasswordAsync_updates_plaintext_mirror_on_success()
    {
        await using var db = CreateDb();
        await SeedSakinRoleAsync(db);
        var userManager = CreateUserManager(db);
        var account = await AddAccountAsync(db, AccountType.Owner);
        var service = new ResidentAccountService(db, userManager);
        await service.EnsureLoginAsync(account);

        var user = await userManager.Users.SingleAsync(x => x.AccountId == account.Id);
        var currentPin = (await db.Accounts.FindAsync(account.Id))!.MobilePassword!;

        var result = await service.ChangeOwnPasswordAsync(user, currentPin, "54321");

        Assert.True(result.Succeeded);
        Assert.Equal("54321", (await db.Accounts.FindAsync(account.Id))!.MobilePassword);
    }

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext db)
    {
        var store = new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(db);
        var services = new ServiceCollection().BuildServiceProvider();

        // Program.cs'teki gercek sifre politikasiyla ayni: Sakin 5 haneli sayisal PIN kullanir.
        var identityOptions = new IdentityOptions();
        identityOptions.Password.RequireDigit = true;
        identityOptions.Password.RequiredLength = 5;
        identityOptions.Password.RequireUppercase = false;
        identityOptions.Password.RequireLowercase = false;
        identityOptions.Password.RequireNonAlphanumeric = false;

        return new UserManager<ApplicationUser>(
            store,
            Options.Create(identityOptions),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            [new PasswordValidator<ApplicationUser>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static async Task SeedSakinRoleAsync(ApplicationDbContext db)
    {
        db.Roles.Add(new IdentityRole(AppRoles.Sakin) { NormalizedName = AppRoles.Sakin.ToUpperInvariant() });
        await db.SaveChangesAsync();
    }

    private static async Task<Account> AddAccountAsync(ApplicationDbContext db, AccountType type)
    {
        var account = new Account { Name = "Test Hesap", AccountType = type, Active = true };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}
