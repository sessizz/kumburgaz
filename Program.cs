using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var turkishCulture = CultureInfo.GetCultureInfo("tr-TR");
CultureInfo.DefaultThreadCurrentCulture = turkishCulture;
CultureInfo.DefaultThreadCurrentUICulture = turkishCulture;

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (connectionString.TrimStart().StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(connectionString);
    }
    else
    {
        options.UseNpgsql(connectionString);
    }
});

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, ApplicationUserClaimsPrincipalFactory>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AppPolicies.SystemAdmin, policy =>
        policy.RequireRole(AppRoles.SistemYonetici));
    options.AddPolicy(AppPolicies.FinanceWrite, policy =>
        policy.RequireRole(AppRoles.SistemYonetici, AppRoles.MuhasebeGorevli));
    options.AddPolicy(AppPolicies.ManagementWrite, policy =>
        policy.RequireRole(AppRoles.SistemYonetici, AppRoles.SiteYonetici));
    options.AddPolicy(AppPolicies.ReportsRead, policy =>
        policy.RequireRole(
            AppRoles.SistemYonetici,
            AppRoles.SiteYonetici,
            AppRoles.MuhasebeGorevli,
            AppRoles.Personel,
            AppRoles.SadeceGoruntuleme));
});

builder.Services.AddScoped<IBillingGroupService, BillingGroupService>();
builder.Services.AddScoped<IDuesGenerationService, DuesGenerationService>();
builder.Services.AddScoped<ICollectionService, CollectionService>();
builder.Services.AddScoped<IReportingService, ReportingService>();
builder.Services.AddScoped<IExpenseForecastService, ExpenseForecastService>();
builder.Services.AddScoped<BalanceDetailedReportService>();
builder.Services.AddScoped<CashBankDetailService>();
builder.Services.AddScoped<ImportBatchService>();
builder.Services.AddScoped<UnitLedgerService>();
builder.Services.AddScoped<UnitStatementService>();
builder.Services.AddScoped<AccountAssignmentService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<ConsistencyCheckService>();
builder.Services.AddScoped<CollectionAllocationRepairService>();
builder.Services.AddHostedService<BackupHostedService>();
builder.Services.AddHostedService<ConsistencyCheckHostedService>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeAreaPage("Identity", "/Account/Register", AppPolicies.SystemAdmin);
});

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (db.Database.IsSqlite())
    {
        db.Database.EnsureCreated();
    }
    else
    {
        db.Database.Migrate();
    }

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    await SeedIdentityAsync(roleManager, userManager);
    await SeedRolePermissionsAsync(db);
}

app.UseHttpsRedirection();
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(turkishCulture),
    SupportedCultures = [turkishCulture],
    SupportedUICultures = [turkishCulture]
});
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "mobile",
    pattern: "m/{controller=Panel}/{action=Index}/{id?}",
    defaults: new { area = "Mobile" })
    .WithStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages().WithStaticAssets();

app.Run();

static async Task SeedIdentityAsync(RoleManager<IdentityRole> roleManager, UserManager<ApplicationUser> userManager)
{
    foreach (var role in AppRoles.All)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    const string adminEmail = "admin@kumburgaz.local";
    var admin = await userManager.FindByEmailAsync(adminEmail);
    if (admin is null)
    {
        admin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            FullName = "Sistem Yöneticisi",
            Title = "Site Yöneticisi"
        };

        var createResult = await userManager.CreateAsync(admin, "Admin123!");
        if (createResult.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, AppRoles.SistemYonetici);
        }
    }
}

// Varsayılan rol yetki matrisini (mevcut sabit davranışı yansıtacak şekilde) tohumlar.
// Idempotent: yalnızca eksik (rol, modül) satırlarını ekler; mevcut ayarları ezmez.
// SistemYonetici matriste tutulmaz — kod tarafında her zaman tam yetkilidir.
static async Task SeedRolePermissionsAsync(ApplicationDbContext db)
{
    // Her rolün varsayılan (yazma yapabildiği) modülleri; ayrıca görüntüleme için ek modüller.
    // Değer: (write modülleri, ek view modülleri)
    var defaults = new (string Role, string[] Write, string[] View)[]
    {
        (AppRoles.SiteYonetici,
            [AppModules.Daireler, AppModules.Hesaplar, AppModules.Duyurular, AppModules.Talepler, AppModules.Belgeler],
            [AppModules.Panel, AppModules.Raporlar]),
        (AppRoles.MuhasebeGorevli,
            [AppModules.Aidatlar, AppModules.Tahsilatlar, AppModules.KasaBanka, AppModules.Muhasebe],
            [AppModules.Panel, AppModules.Raporlar]),
        (AppRoles.Personel,
            [],
            [AppModules.Panel, AppModules.Raporlar]),
        (AppRoles.SadeceGoruntuleme,
            [],
            [AppModules.Panel, AppModules.Raporlar]),
    };

    var existing = await db.RolePermissions
        .Select(x => new { x.RoleName, x.Module })
        .ToListAsync();
    var have = existing.Select(x => (x.RoleName, x.Module)).ToHashSet();

    var toAdd = new List<RolePermission>();
    foreach (var (role, write, view) in defaults)
    {
        var writeSet = write.ToHashSet();
        foreach (var module in write.Concat(view).Distinct())
        {
            if (have.Contains((role, module)))
            {
                continue;
            }

            toAdd.Add(new RolePermission
            {
                RoleName = role,
                Module = module,
                CanView = true,
                CanWrite = writeSet.Contains(module)
            });
        }
    }

    if (toAdd.Count > 0)
    {
        db.RolePermissions.AddRange(toAdd);
        await db.SaveChangesAsync();
    }
}
