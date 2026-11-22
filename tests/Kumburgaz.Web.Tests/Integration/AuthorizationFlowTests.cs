using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Validation;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kumburgaz.Web.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class AuthorizationFlowTests(KumburgazAppFactory factory)
{
    private const string Password = "test12345";
    private const string McpResource = "https://localhost/mcp";

    private readonly OAuthTestClient _oauth = new(factory);

    private static string NewUserName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    [Fact]
    public async Task Staff_user_completes_code_flow_and_gets_tokens()
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);

        var tokens = await _oauth.GetTokensAsync(userName, Password);

        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
        // OpenIddict oluşturma zamanını saniye hassasiyetinde saklar ("r" biçimi) ve expires_in'i
        // o andan hesaplar; saniyenin ikinci yarısında üretilen token 3599 döner.
        Assert.InRange(tokens.ExpiresIn, 3599, 3600);
    }

    // Token'daki kimlik: AuditLog görünen adı "(Claude)" ekiyle okur, uygulama kodu NameIdentifier'ı.
    [Fact]
    public async Task Access_token_carries_user_identity_roles_and_claude_display_name()
    {
        var userName = NewUserName("muhasebe");
        var user = await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);

        var tokens = await _oauth.GetTokensAsync(userName, Password);
        var principal = await ValidateAccessTokenAsync(tokens.AccessToken);

        Assert.Equal(user.Id, principal.GetClaim(Claims.Subject));
        Assert.Equal(user.Id, principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(userName, principal.Identity!.Name);
        Assert.Equal($"{userName} Test (Claude)",
            principal.FindFirstValue(ApplicationUserClaimsPrincipalFactory.DisplayNameClaimType));
        Assert.True(principal.IsInRole(AppRoles.MuhasebeGorevli));
        Assert.True(principal.HasScope("mcp"));
        Assert.Equal(McpResource, Assert.Single(principal.GetAudiences()));
    }

    // Review Focus 1: claude.ai RFC 8707 resource gönderir; audience her zaman kanonik /mcp adresidir.
    [Theory]
    [InlineData("https://localhost/mcp")]
    [InlineData("https://localhost/mcp/")]
    public async Task Resource_parameter_is_accepted(string resource)
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);

        var tokens = await _oauth.GetTokensAsync(userName, Password, resource);
        var principal = await ValidateAccessTokenAsync(tokens.AccessToken);

        Assert.Equal(McpResource, Assert.Single(principal.GetAudiences()));
        Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
    }

    [Fact]
    public async Task Unknown_resource_is_rejected()
    {
        var clientId = await _oauth.RegisterClientAsync();
        var (url, _) = OAuthTestClient.CreateAuthorizeRequest(clientId, "https://evil.example/mcp");

        var response = await _oauth.CreateBrowser().GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(Errors.InvalidTarget, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sakin_user_is_denied()
    {
        var userName = NewUserName("sakin");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.Sakin);
        var clientId = await _oauth.RegisterClientAsync();

        var result = await _oauth.AuthorizeAsync(_oauth.CreateBrowser(), clientId, userName, Password);

        Assert.Equal(Errors.AccessDenied, result.Error);
        Assert.Null(result.Code);
        Assert.False(result.ConsentShown);
    }

    [Fact]
    public async Task User_without_roles_is_denied()
    {
        var userName = NewUserName("rolsuz");
        await _oauth.CreateUserAsync(userName, Password);
        var clientId = await _oauth.RegisterClientAsync();

        var result = await _oauth.AuthorizeAsync(_oauth.CreateBrowser(), clientId, userName, Password);

        Assert.Equal(Errors.AccessDenied, result.Error);
        Assert.Null(result.Code);
    }

    [Fact]
    public async Task Denied_consent_returns_access_denied()
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var clientId = await _oauth.RegisterClientAsync();

        var result = await _oauth.AuthorizeAsync(_oauth.CreateBrowser(), clientId, userName, Password, deny: true);

        Assert.True(result.ConsentShown);
        Assert.Equal(Errors.AccessDenied, result.Error);
        Assert.Null(result.Code);
    }

    // Onay formu authorize sorgusunu gizli alan olarak taşır; sorguya eklenen submit=accept "Reddet"i ezmemeli.
    [Fact]
    public async Task Submit_parameter_in_authorize_url_cannot_override_deny()
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var clientId = await _oauth.RegisterClientAsync();

        var result = await _oauth.AuthorizeAsync(_oauth.CreateBrowser(), clientId, userName, Password, deny: true,
            customize: p => p["submit"] = "accept");

        Assert.True(result.ConsentShown);
        Assert.Equal(Errors.AccessDenied, result.Error);
        Assert.Null(result.Code);
    }

    [Fact]
    public async Task Consent_page_shows_client_name_and_notice()
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var clientName = $"Claude <Deneme> {Guid.NewGuid():N}";
        var clientId = await _oauth.RegisterClientAsync(clientName);

        var result = await _oauth.AuthorizeAsync(_oauth.CreateBrowser(), clientId, userName, Password);

        Assert.NotNull(result.ConsentHtml);
        // İstemci adı kodlanmış olarak basılır (DCR'de istemci adını istemci belirler).
        Assert.DoesNotContain("<Deneme>", result.ConsentHtml);
        var html = WebUtility.HtmlDecode(result.ConsentHtml);
        Assert.Contains(clientName, html);
        Assert.Contains(userName, html);
        Assert.Contains(
            "Claude, sizin yetkilerinizle verileri okuyabilecek ve sizin onayınızla tahsilat/gelir/gider kaydı açabilecek.",
            html);
    }

    [Fact]
    public async Task Token_request_without_code_verifier_fails()
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var clientId = await _oauth.RegisterClientAsync();
        var authorization = await _oauth.AuthorizeAsync(_oauth.CreateBrowser(), clientId, userName, Password);
        Assert.NotNull(authorization.Code);

        var response = await _oauth.ExchangeCodeAsync(clientId, authorization.Code!, codeVerifier: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Yalnız S256: "plain" ve yöntemsiz (plain varsayılan) challenge reddedilir. Kullanıcı girişli ve
    // izni kayıtlı olduğundan doğrulama atlansaydı onay ekranı olmadan doğrudan code dönerdi.
    [Theory]
    [InlineData("plain")]
    [InlineData(null)]
    public async Task Authorize_rejects_non_s256_code_challenge(string? method)
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var clientId = await _oauth.RegisterClientAsync();
        var browser = _oauth.CreateBrowser();
        Assert.NotNull((await _oauth.AuthorizeAsync(browser, clientId, userName, Password)).Code);

        var (url, _) = OAuthTestClient.CreateAuthorizeRequest(clientId, customize: p =>
        {
            p["code_challenge"] = "plain-verifier-0123456789-0123456789-0123456789";
            p["code_challenge_method"] = method;
        });
        var response = await browser.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(Errors.InvalidRequest, await response.Content.ReadAsStringAsync());
        Assert.Null(response.Headers.Location);
    }

    // Review Focus 2: onaydan sonra rolü Sakin'e düşen kullanıcı yeni token alamaz.
    [Fact]
    public async Task Refresh_fails_after_user_loses_staff_role()
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var tokens = await _oauth.GetTokensAsync(userName, Password);

        await WithUserAsync(userName, async (users, user) =>
        {
            await users.RemoveFromRolesAsync(user, await users.GetRolesAsync(user));
            await users.AddToRoleAsync(user, AppRoles.Sakin);
        });
        var response = await _oauth.RefreshAsync(tokens.ClientId, tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Errors.InvalidGrant, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task Refresh_fails_after_user_is_locked_out()
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var tokens = await _oauth.GetTokensAsync(userName, Password);

        await WithUserAsync(userName, async (users, user) =>
        {
            await users.SetLockoutEnabledAsync(user, true);
            await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
        });
        var response = await _oauth.RefreshAsync(tokens.ClientId, tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Errors.InvalidGrant, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task Refresh_issues_new_tokens_for_eligible_user()
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var tokens = await _oauth.GetTokensAsync(userName, Password);

        var refreshed = await OAuthTestClient.ReadTokensAsync(
            tokens.ClientId, await _oauth.RefreshAsync(tokens.ClientId, tokens.RefreshToken));
        var original = await ValidateAccessTokenAsync(tokens.AccessToken);
        var principal = await ValidateAccessTokenAsync(refreshed.AccessToken);

        Assert.False(string.IsNullOrWhiteSpace(refreshed.RefreshToken));
        Assert.Equal(McpResource, Assert.Single(principal.GetAudiences()));
        Assert.True(principal.IsInRole(AppRoles.MuhasebeGorevli));
        // Bağlantı iptali yenilenen token'ları da kapsasın: onaydaki kalıcı yetkiye bağlı kalır.
        Assert.False(string.IsNullOrEmpty(original.GetAuthorizationId()));
        Assert.Equal(original.GetAuthorizationId(), principal.GetAuthorizationId());
    }

    [Fact]
    public async Task Second_authorization_skips_consent()
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var clientId = await _oauth.RegisterClientAsync();

        var first = await _oauth.AuthorizeAsync(_oauth.CreateBrowser(), clientId, userName, Password);
        var second = await _oauth.AuthorizeAsync(_oauth.CreateBrowser(), clientId, userName, Password);

        Assert.True(first.ConsentShown);
        Assert.NotNull(first.Code);
        Assert.False(second.ConsentShown);
        Assert.NotNull(second.Code);
    }

    private async Task<ClaimsPrincipal> ValidateAccessTokenAsync(string accessToken)
    {
        using var scope = factory.Services.CreateScope();
        var validation = scope.ServiceProvider.GetRequiredService<OpenIddictValidationService>();
        return await validation.ValidateAccessTokenAsync(accessToken);
    }

    private async Task WithUserAsync(string userName, Func<UserManager<ApplicationUser>, ApplicationUser, Task> change)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByNameAsync(userName) ?? throw new InvalidOperationException(userName);
        await change(users, user);
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("error").GetString();
    }
}
