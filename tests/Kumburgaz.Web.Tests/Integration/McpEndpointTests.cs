using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Kumburgaz.Web.Auth;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenIddict.Abstractions;
using Xunit;

namespace Kumburgaz.Web.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class McpEndpointTests(KumburgazAppFactory factory)
{
    private const string Password = "test12345";
    private const string GuideUri = "kumburgaz://rehber";

    private readonly OAuthTestClient _oauth = new(factory);

    // claude.ai bağlayıcısı yetkilendirme sunucusunu bu başlıktan bulur.
    [Fact]
    public async Task Mcp_without_token_returns_401_with_resource_metadata()
    {
        var response = await _oauth.InitializeMcpAsync();

        AssertMcpChallenge(response);
    }

    // Gövdesiz istek (ör. curl -X POST) de 415 değil, keşif başlığıyla 401 almalı.
    [Fact]
    public async Task Mcp_without_token_and_body_returns_401()
    {
        var response = await _oauth.CreateBrowser().PostAsync("/mcp", content: null);

        AssertMcpChallenge(response);
    }

    [Fact]
    public async Task Mcp_with_invalid_token_returns_401_with_resource_metadata()
    {
        var response = await _oauth.InitializeMcpAsync("gecersiz-token");

        AssertMcpChallenge(response);
    }

    // Uygulamanın giriş çerezi /mcp'yi açmaz; yalnız bearer token geçerlidir.
    [Fact]
    public async Task Mcp_does_not_accept_login_cookie()
    {
        var userName = await CreateUserAsync(AppRoles.SistemYonetici);
        var browser = _oauth.CreateBrowser();
        await OAuthTestClient.LoginAsync(browser, userName, Password);

        var response = await _oauth.InitializeMcpAsync(browser: browser);

        AssertMcpChallenge(response);
    }

    // Varsayılan şema Identity çerezidir: sayfalar 401 değil giriş yönlendirmesi vermeyi sürdürür.
    [Fact]
    public async Task Pages_still_redirect_anonymous_users_to_login()
    {
        var response = await _oauth.CreateBrowser().GetAsync("/Units");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", response.Headers.Location!.OriginalString);
    }

    // RFC 9728: hem kök adres hem 401 başlığındaki /mcp sonekli adres aynı belgeyi verir.
    [Theory]
    [InlineData("/.well-known/oauth-protected-resource")]
    [InlineData("/.well-known/oauth-protected-resource/mcp")]
    public async Task Protected_resource_metadata_points_to_this_server(string path)
    {
        var response = await _oauth.CreateBrowser().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal([KumburgazAppFactory.Issuer], Strings(root.GetProperty("authorization_servers")));
        Assert.Equal("https://localhost/mcp", root.GetProperty("resource").GetString());
        Assert.Equal([OAuthConstants.Scope], Strings(root.GetProperty("scopes_supported")));
        Assert.Equal("Kumburgaz", root.GetProperty("resource_name").GetString());
    }

    [Theory]
    [InlineData(OAuthConstants.Scope, true)]
    [InlineData("baska", false)]
    [InlineData(null, false)]
    public async Task Mcp_policy_requires_mcp_scope(string? scope, bool expected)
    {
        var user = await _oauth.CreateUserAsync($"mcp-{Guid.NewGuid():N}", Password, AppRoles.MuhasebeGorevli);
        var identity = new ClaimsIdentity("Bearer", "name", "role");
        identity.SetClaim(OpenIddictConstants.Claims.Subject, user.Id);
        if (scope is not null)
        {
            identity.SetScopes(scope);
        }

        using var services = factory.Services.CreateScope();
        var authorization = services.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var result = await authorization.AuthorizeAsync(new ClaimsPrincipal(identity), resource: null, OAuthConstants.McpPolicy);

        Assert.Equal(expected, result.Succeeded);
    }

    // Token geçerli olsa da kullanıcı artık uygun değilse erişim, token süresini beklemeden biter.
    [Fact]
    public Task Locked_out_user_with_valid_token_is_refused()
        => AssertRefusedAfterAsync(userName => _oauth.LockOutAsync(userName));

    [Fact]
    public Task Deleted_user_with_valid_token_is_refused()
        => AssertRefusedAfterAsync(userName => _oauth.WithUserAsync(
            userName, (users, user) => users.DeleteAsync(user)));

    [Fact]
    public Task Staff_user_given_sakin_role_with_valid_token_is_refused()
        => AssertRefusedAfterAsync(userName => _oauth.WithUserAsync(
            userName, (users, user) => users.AddToRoleAsync(user, AppRoles.Sakin)));

    [Fact]
    public Task User_who_lost_staff_role_with_valid_token_is_refused()
        => AssertRefusedAfterAsync(userName => _oauth.WithUserAsync(
            userName, (users, user) => users.RemoveFromRoleAsync(user, AppRoles.MuhasebeGorevli)));

    [Fact]
    public async Task Authenticated_client_lists_tools_and_reads_instructions()
    {
        await using var client = await ConnectAsync(AppRoles.MuhasebeGorevli);

        var tools = await client.ListToolsAsync();
        var categories = Assert.Single(tools, x => x.Name == "categories");
        Assert.True(categories.ProtocolTool.Annotations?.ReadOnlyHint);
        // Kullanıcı ve servisler sunucuda bağlanır; istemciye yalnız gerçek girdiler açılır.
        var parameters = categories.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(x => x.Name);
        Assert.Equal(["kind"], parameters);

        Assert.Equal("Kumburgaz", client.ServerInfo?.Name);
        Assert.Contains("₺2.500,00", client.ServerInstructions);
        Assert.Contains(GuideUri, client.ServerInstructions);
    }

    // SDK istemcisi daha yeni protokolü seçebilir; initialize ile bağlanan istemci de aynı talimatları almalı.
    [Fact]
    public async Task Initialize_handshake_returns_instructions()
    {
        var tokens = await _oauth.GetTokensAsync(await CreateUserAsync(AppRoles.Personel), Password);

        var response = await _oauth.InitializeMcpAsync(tokens.AccessToken);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        var data = Assert.Single(body.Split('\n'), x => x.StartsWith("data:", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(data["data:".Length..]);
        var result = json.RootElement.GetProperty("result");
        Assert.Equal("2025-06-18", result.GetProperty("protocolVersion").GetString());
        Assert.Equal("Kumburgaz", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Contains("₺2.500,00", result.GetProperty("instructions").GetString());
    }

    [Fact]
    public async Task Guide_resource_explains_core_concepts()
    {
        await using var client = await ConnectAsync(AppRoles.Personel);

        var resource = Assert.Single(await client.ListResourcesAsync(), x => x.Uri == GuideUri);
        Assert.Equal("text/markdown", resource.MimeType);

        var content = await client.ReadResourceAsync(GuideUri);
        var text = Assert.IsType<TextResourceContents>(Assert.Single(content.Contents)).Text;
        Assert.All(
            ["Aidat grubu", "Birleşik daire", "Devir bakiyesi", "Avans", "Mahsup", "cash:{id}", "bank:{id}", "2026-2027"],
            concept => Assert.Contains(concept, text));
    }

    // Muhasebe görevlisi sistem yöneticisi değildir: token'daki rol yetki matrisinde bulunmalı.
    [Fact]
    public async Task Categories_lists_active_categories_with_kind()
    {
        var (income, expense, inactive) = await SeedCategoriesAsync();
        await using var client = await ConnectAsync(AppRoles.MuhasebeGorevli);

        var all = await CallCategoriesAsync(client, kind: null);
        Assert.Equal("income", all[income.Id].Kind);
        Assert.Equal(income.Name, all[income.Id].Name);
        Assert.Equal("expense", all[expense.Id].Kind);
        Assert.DoesNotContain(inactive.Id, all.Keys);

        var incomes = await CallCategoriesAsync(client, "income");
        Assert.Contains(income.Id, incomes.Keys);
        Assert.All(incomes.Values, x => Assert.Equal("income", x.Kind));

        var expenses = await CallCategoriesAsync(client, "expense");
        Assert.Contains(expense.Id, expenses.Keys);
        Assert.All(expenses.Values, x => Assert.Equal("expense", x.Kind));
    }

    // Araç sonucu Claude'a düz metin gider: Türkçe harfler Unicode kaçış dizisi değil, olduğu gibi görünmeli.
    [Fact]
    public async Task Tool_result_text_keeps_turkish_letters()
    {
        var (_, expense, _) = await SeedCategoriesAsync();
        await using var client = await ConnectAsync(AppRoles.MuhasebeGorevli);

        var result = await client.CallToolAsync("categories");

        Assert.Contains(expense.Name, Text(result));
    }

    [Fact]
    public async Task Categories_rejects_unknown_kind()
    {
        await using var client = await ConnectAsync(AppRoles.MuhasebeGorevli);

        var result = await client.CallToolAsync("categories", new Dictionary<string, object?> { ["kind"] = "hepsi" });

        Assert.True(result.IsError);
        Assert.Contains("income", Text(result));
    }

    // Personel'in yetki matrisinde Muhasebe yok (yalnız Panel ve Raporlar).
    [Fact]
    public async Task Tool_rejects_user_without_module_permission()
    {
        await using var client = await ConnectAsync(AppRoles.Personel);

        var result = await client.CallToolAsync("categories");

        Assert.True(result.IsError);
        Assert.Contains("yetkiniz yok", Text(result));
        Assert.Contains(AppModules.Muhasebe, Text(result));
    }

    private sealed record CategoryResult(int Id, string Name, string Kind);

    private static async Task<Dictionary<int, CategoryResult>> CallCategoriesAsync(McpClient client, string? kind)
    {
        var arguments = kind is null ? null : new Dictionary<string, object?> { ["kind"] = kind };
        var result = await client.CallToolAsync("categories", arguments);

        Assert.True(result.IsError is not true, Text(result));
        return JsonSerializer.Deserialize<List<CategoryResult>>(Text(result), JsonSerializerOptions.Web)!.ToDictionary(x => x.Id);
    }

    private static string Text(CallToolResult result)
        => string.Join('\n', result.Content.OfType<TextContentBlock>().Select(x => x.Text));

    private async Task<(IncomeExpenseCategory Income, IncomeExpenseCategory Expense, IncomeExpenseCategory Inactive)> SeedCategoriesAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var income = new IncomeExpenseCategory { Name = $"Kira {Guid.NewGuid():N}", Type = CategoryTypeHelper.Gelir };
        var expense = new IncomeExpenseCategory { Name = $"Bakım {Guid.NewGuid():N}", Type = CategoryTypeHelper.Gider };
        var inactive = new IncomeExpenseCategory { Name = $"Eski {Guid.NewGuid():N}", Type = CategoryTypeHelper.Gider, Active = false };
        db.IncomeExpenseCategories.AddRange(income, expense, inactive);
        await db.SaveChangesAsync();
        return (income, expense, inactive);
    }

    private async Task<string> CreateUserAsync(string role)
    {
        var userName = $"mcp-{Guid.NewGuid():N}";
        await _oauth.CreateUserAsync(userName, Password, role);
        return userName;
    }

    private async Task<McpClient> ConnectAsync(string role)
    {
        var tokens = await _oauth.GetTokensAsync(await CreateUserAsync(role), Password);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("https://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {tokens.AccessToken}" }
            },
            _oauth.CreateBrowser(),
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    // Değişiklikten önce 200, sonra 403; aynı anda bağlı başka kullanıcı etkilenmez.
    private async Task AssertRefusedAfterAsync(Func<string, Task> change)
    {
        var userName = await CreateUserAsync(AppRoles.MuhasebeGorevli);
        var tokens = await _oauth.GetTokensAsync(userName, Password);
        var otherTokens = await _oauth.GetTokensAsync(await CreateUserAsync(AppRoles.MuhasebeGorevli), Password);
        Assert.Equal(HttpStatusCode.OK, (await _oauth.InitializeMcpAsync(tokens.AccessToken)).StatusCode);

        await change(userName);

        Assert.Equal(HttpStatusCode.Forbidden, (await _oauth.InitializeMcpAsync(tokens.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _oauth.InitializeMcpAsync(otherTokens.AccessToken)).StatusCode);
    }

    private static void AssertMcpChallenge(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(
            "resource_metadata=\"https://localhost/.well-known/oauth-protected-resource",
            response.Headers.WwwAuthenticate.ToString());
    }

    private static IEnumerable<string?> Strings(JsonElement array)
        => array.EnumerateArray().Select(x => x.GetString());
}
