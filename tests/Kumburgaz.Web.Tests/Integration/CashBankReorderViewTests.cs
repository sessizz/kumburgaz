using System.Net;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kumburgaz.Web.Tests.Integration;

// Kasa/banka detayında gün içi sürükle-bırak: işaretlemenin yalnız izinli ve filtresiz görünümde çıkması.
[Collection(IntegrationCollection.Name)]
public class CashBankReorderViewTests(KumburgazAppFactory factory)
{
    private const string Password = "test12345";
    private static readonly DateTime Day = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
    private readonly OAuthTestClient _oauth = new(factory);

    [Fact]
    public async Task Detail_marks_days_and_rows_for_reorder_when_allowed()
    {
        var (cashId, firstId, secondId) = await SeedAsync();
        var browser = await LoginAsync(AppRoles.SistemYonetici);

        var html = await browser.GetStringAsync($"/CashBank/CashBox/{cashId}");

        Assert.Contains("data-reorder-day=\"2026-11-23\"", html);
        Assert.Contains($"data-tx=\"ledger:{firstId}\"", html);
        Assert.Contains($"data-tx=\"ledger:{secondId}\"", html);
        Assert.Contains("data-reorder-handle", html);
        Assert.Contains("drag_indicator", html);
    }

    [Theory]
    [InlineData("?Q=x")]
    [InlineData("?Type=cikis")]
    public async Task Detail_hides_reorder_when_searching_or_filtered(string query)
    {
        var (cashId, _, _) = await SeedAsync("x");
        var browser = await LoginAsync(AppRoles.SistemYonetici);

        var html = await browser.GetStringAsync($"/CashBank/CashBox/{cashId}{query}");

        Assert.Contains("data-tx=", html);
        Assert.DoesNotContain("data-reorder-handle", html);
        Assert.DoesNotContain("drag_indicator", html);
    }

    [Fact]
    public async Task Detail_hides_reorder_for_read_only_user()
    {
        var (cashId, _, _) = await SeedAsync();
        await GrantViewOnlyAsync(AppRoles.SadeceGoruntuleme);
        var browser = await LoginAsync(AppRoles.SadeceGoruntuleme);

        var response = await browser.GetAsync($"/CashBank/CashBox/{cashId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-tx=", html);
        Assert.DoesNotContain("data-reorder-handle", html);
    }

    [Fact]
    public async Task Detail_loads_sortable_and_reorder_scripts()
    {
        var (cashId, _, _) = await SeedAsync();
        var browser = await LoginAsync(AppRoles.SistemYonetici);

        var html = await browser.GetStringAsync($"/CashBank/CashBox/{cashId}");

        Assert.Contains("/lib/sortablejs/Sortable.min.js", html);
        Assert.Contains("/js/cashbank-reorder.js", html);
        Assert.True(html.IndexOf("Sortable.min.js", StringComparison.Ordinal) < html.IndexOf("cashbank-reorder.js", StringComparison.Ordinal));
    }

    // Kasa/Banka için yalnız görüntüleme yetkisi (yazma yok).
    private async Task GrantViewOnlyAsync(string role)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.RolePermissions.Add(new RolePermission { RoleName = role, Module = AppModules.KasaBanka, CanView = true });
        await db.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<Kumburgaz.Web.Services.PermissionService>().Invalidate();
    }

    private async Task<HttpClient> LoginAsync(string role)
    {
        var userName = $"reorder-view-{Guid.NewGuid():N}";
        await _oauth.CreateUserAsync(userName, Password, role);
        var browser = _oauth.CreateBrowser();
        using var login = await OAuthTestClient.LoginAsync(browser, userName, Password);
        return browser;
    }

    private async Task<(int CashId, int FirstId, int SecondId)> SeedAsync(string description = "İşlem")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cash = new CashBox { Name = "Görünüm kasası", OpeningBalanceDate = Day };
        var first = new LedgerTransaction { CashBox = cash, Date = Day.AddTicks(10), Amount = 100m, Description = $"{description} bir" };
        var second = new LedgerTransaction { CashBox = cash, Date = Day.AddTicks(20), Amount = 40m, Description = $"{description} iki" };
        db.LedgerTransactions.AddRange(first, second);
        await db.SaveChangesAsync();
        return (cash.Id, first.Id, second.Id);
    }
}
