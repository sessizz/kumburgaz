using Kumburgaz.Web.Auth;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Mcp;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

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

    // OpenIddict istemci/yetki/token tabloları aynı veritabanında.
    options.UseOpenIddict();
});

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Anahtarları veritabanında sakla: konteyner her deploy'da sıfırdan başladığı
// için dosya sistemine yazılan varsayılan anahtar halkası kaybolur ve mevcut
// login çerezlerinin şifresi çözülemez hale gelir, herkes oturumdan atılır.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<ApplicationDbContext>()
    .SetApplicationName("Kumburgaz");

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = true;
        // Sakin girişleri 5 haneli sayısal PIN kullanır (kullanıcının açık tercihi);
        // bu yüzden büyük/küçük harf ve sembol zorunluluğu kapalı.
        options.Password.RequiredLength = 5;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// claude.ai MCP bağlayıcısı için OAuth yetkilendirme sunucusu (OpenIddict).
builder.Services.AddKumburgazOAuthServer(builder.Configuration);
builder.Services.AddKumburgazOAuthRateLimits();
builder.Services.AddKumburgazMcp(builder.Configuration);

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
builder.Services.AddScoped<IDuesLedgerRowService, DuesLedgerRowService>();
builder.Services.AddScoped<IReportingService, ReportingService>();
builder.Services.AddScoped<IExpenseForecastService, ExpenseForecastService>();
builder.Services.AddScoped<BalanceDetailedReportService>();
builder.Services.AddScoped<CashBankDetailService>();
builder.Services.AddScoped<DashboardFiguresService>();
builder.Services.AddScoped<LedgerEntryService>();
builder.Services.AddScoped<ImportBatchService>();
builder.Services.AddScoped<UnitLedgerService>();
builder.Services.AddScoped<UnitStatementService>();
builder.Services.AddScoped<AccountAssignmentService>();
builder.Services.AddScoped<MobileScopeService>();
builder.Services.AddScoped<ResidentAccountService>();
builder.Services.AddScoped<SakinAreaRestrictionFilter>();
builder.Services.AddScoped<ImageAttachmentService>();
builder.Services.AddScoped<DocumentFileService>();
builder.Services.AddScoped<MahsupService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<PushSenderService>();
builder.Services.AddSingleton<PushQueue>();
builder.Services.AddSingleton<CaptureSessionService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<ConsistencyCheckService>();
builder.Services.AddScoped<CollectionAllocationRepairService>();
builder.Services.AddHostedService<BackupHostedService>();
builder.Services.AddHostedService<ConsistencyCheckHostedService>();
builder.Services.AddHostedService<PushDispatchHostedService>();

builder.Services.AddControllersWithViews(options =>
{
    // Sakin rolü masaüstü controller'lara erişemez (gerçek sunucu sınırı).
    options.Filters.Add<SakinAreaRestrictionFilter>();
    // tr-TR binder'ı "1234.56" girişini 123456 yapıyordu; tutarlar iki biçimde de okunur.
    options.UseFlexibleDecimalBinding();
});
builder.Services.AddRazorPages(options =>
{
    // Hesaplar yalnız Sistem Kullanıcıları ekranından açılır.
    options.DisableSelfRegistration();
});
builder.Services.AddHealthChecks();

// Uygulama Cloudflare Tunnel + Traefik arkasında düz HTTP alır. Şema/istemci IP'si
// proxy başlıklarından okunmazsa yönlendirmeler http:// olur, çerezler Secure
// işaretlenmez ve HSTS hiç gönderilmez. Konteyner portu dışarı açık değil; tek
// gelen trafik Traefik'ten olduğu için proxy adresi kısıtlanmıyor.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    // Traefik, Cloudflare'in X-Forwarded-Proto değerini kendi (http) değeriyle ezebilir;
    // Cloudflare'in CF-Visitor başlığı ziyaretçinin gerçek şemasını taşır.
    if (!context.Request.IsHttps
        && context.Request.Headers["CF-Visitor"].ToString().Contains("\"scheme\":\"https\"", StringComparison.OrdinalIgnoreCase))
    {
        context.Request.Scheme = "https";
    }

    await next();
});

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
        await EnsureSqliteDatabaseUsableAsync(db);
        db.Database.EnsureCreated();
    }
    else
    {
        db.Database.Migrate();
    }

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    await SeedIdentityAsync(roleManager, userManager, app.Configuration["Seed:AdminPassword"], app.Logger);
    await SeedRolePermissionsAsync(db);
    var residentAccountService = scope.ServiceProvider.GetRequiredService<ResidentAccountService>();
    await SeedResidentAccountsAsync(db, residentAccountService);

    // Devir borcuna daha once tahsis edilmis ama hic kalici kayda donusturulmemis tahsilat
    // fazlasini gercek CollectionAllocation satirlarina cevirir (bkz. AddDevirOpeningBalanceAllocation
    // migration'i). Idempotent - zaten uzlasmis veride hicbir etkisi olmaz.
    await CollectionAdvanceAllocator.ApplyAsync(db);
    await TransferPairing.BackfillAsync(db, app.Logger);

    // OAuth imzalama/şifreleme anahtarlarını açılışta yükle (yoksa üret, bkz. OAuthKeyStore):
    // anahtar ya da veritabanı sorunu ilk istekte değil burada görünsün.
    _ = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
}

app.UseHttpsRedirection();
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(turkishCulture),
    SupportedCultures = [turkishCulture],
    SupportedUICultures = [turkishCulture]
});

app.Use(async (context, next) =>
{
    if ((context.Request.Path == PathString.Empty || context.Request.Path == "/")
        && IsMobileUserAgent(context.Request.Headers.UserAgent.ToString()))
    {
        context.Response.Redirect("/m");
        return;
    }

    await next();
});

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapAreaControllerRoute(
    name: "mobile",
    areaName: "Mobile",
    pattern: "m/{controller=Panel}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages().WithStaticAssets();
app.MapHealthChecks("/health");
app.MapKumburgazMcp();

app.Run();

static async Task SeedIdentityAsync(
    RoleManager<IdentityRole> roleManager,
    UserManager<ApplicationUser> userManager,
    string? configuredAdminPassword,
    ILogger logger)
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

        var (password, generated) = AdminSeedPassword.Resolve(configuredAdminPassword);
        var createResult = await userManager.CreateAsync(admin, password);
        if (createResult.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, AppRoles.SistemYonetici);
            if (generated)
            {
                logger.LogWarning(
                    "İlk yönetici hesabı oluşturuldu: {Email} / geçici şifre: {Password}. Girişten sonra değiştirin " +
                    "veya Seed__AdminPassword ortam değişkenini verin.",
                    adminEmail, password);
            }
        }
    }
    else if (await userManager.CheckPasswordAsync(admin, AdminSeedPassword.LegacyDefault))
    {
        logger.LogCritical(
            "Yönetici hesabı {Email} hâlâ kaynak kodda yazılı varsayılan şifreyi kullanıyor. Hemen değiştirin.",
            adminEmail);
    }
}

static bool IsMobileUserAgent(string? userAgent)
{
    if (string.IsNullOrWhiteSpace(userAgent))
    {
        return false;
    }

    var ua = userAgent.AsSpan();
    return ua.Contains("Mobi", StringComparison.OrdinalIgnoreCase)
        || ua.Contains("Android", StringComparison.OrdinalIgnoreCase)
        || ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
        || ua.Contains("iPad", StringComparison.OrdinalIgnoreCase)
        || ua.Contains("iPod", StringComparison.OrdinalIgnoreCase)
        || ua.Contains("Windows Phone", StringComparison.OrdinalIgnoreCase);
}

static async Task EnsureSqliteDatabaseUsableAsync(ApplicationDbContext db)
{
    var connection = db.Database.GetDbConnection();
    if (connection is not SqliteConnection sqliteConnection)
    {
        return;
    }

    var dataSource = sqliteConnection.DataSource;
    if (string.IsNullOrWhiteSpace(dataSource) || dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    var dbPath = Path.GetFullPath(dataSource);
    if (!File.Exists(dbPath))
    {
        return;
    }

    await sqliteConnection.OpenAsync();
    try
    {
        var hasCoreTables = await SqliteTableExistsAsync(sqliteConnection, "Sites")
            && await SqliteTableExistsAsync(sqliteConnection, "Units")
            && await SqliteTableExistsAsync(sqliteConnection, "DuesInstallments");

        if (hasCoreTables)
        {
            return;
        }
    }
    finally
    {
        await sqliteConnection.CloseAsync();
    }

    SqliteConnection.ClearAllPools();
    var backupPath = $"{dbPath}.incomplete-{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
    File.Move(dbPath, backupPath, overwrite: false);

    foreach (var suffix in new[] { "-wal", "-shm" })
    {
        var sidecar = dbPath + suffix;
        if (File.Exists(sidecar))
        {
            File.Move(sidecar, backupPath + suffix, overwrite: false);
        }
    }
}

static async Task<bool> SqliteTableExistsAsync(SqliteConnection connection, string tableName)
{
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
    command.Parameters.AddWithValue("$name", tableName);
    var result = await command.ExecuteScalarAsync();
    return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
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
        // Sakin yalnızca mobil alanda çalışır; Muhasebe write mahsuplu gider içindir (Aşama 3).
        (AppRoles.Sakin,
            [AppModules.Muhasebe, AppModules.Talepler],
            [AppModules.Panel, AppModules.Daireler, AppModules.Aidatlar, AppModules.Duyurular]),
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

// Mevcut tüm Malik/Kiracı hesapları için mobil giriş + 5 haneli PIN üretir (idempotent).
static async Task SeedResidentAccountsAsync(ApplicationDbContext db, ResidentAccountService residentAccountService)
{
    var accounts = await db.Accounts
        .Where(x => x.AccountType == AccountType.Owner || x.AccountType == AccountType.Tenant)
        .ToListAsync();

    foreach (var account in accounts)
    {
        await residentAccountService.EnsureLoginAsync(account);
    }
}
