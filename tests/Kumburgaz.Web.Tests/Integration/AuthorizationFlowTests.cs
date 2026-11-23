using System.Buffers.Text;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Kumburgaz.Web.Auth;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.WebUtilities;
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

    // Sakin ekranda hiçbir masaüstü sayfasına giremez; personel rolü de olsa bağlayıcıyı kullanamaz.
    [Fact]
    public async Task Staff_user_with_sakin_role_is_denied()
    {
        var userName = NewUserName("personel-sakin");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli, AppRoles.Sakin);
        var clientId = await _oauth.RegisterClientAsync();

        var result = await _oauth.AuthorizeAsync(_oauth.CreateBrowser(), clientId, userName, Password);

        Assert.Equal(Errors.AccessDenied, result.Error);
        Assert.Null(result.Code);
        Assert.False(result.ConsentShown);
    }

    [Fact]
    public async Task Code_exchange_fails_after_staff_user_is_given_sakin_role()
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var clientId = await _oauth.RegisterClientAsync();
        var authorization = await _oauth.AuthorizeAsync(_oauth.CreateBrowser(), clientId, userName, Password);
        Assert.NotNull(authorization.Code);

        await _oauth.WithUserAsync(userName, (users, user) => users.AddToRoleAsync(user, AppRoles.Sakin));
        var response = await _oauth.ExchangeCodeAsync(clientId, authorization.Code!, authorization.CodeVerifier);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Errors.InvalidGrant, await ReadErrorAsync(response));
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
    // ASP.NET Core sorgu/form anahtarları büyük-küçük harf duyarsızdır: "Submit" ve "SUBMIT" de aynı alandır.
    [Theory]
    [InlineData("submit")]
    [InlineData("Submit")]
    [InlineData("SUBMIT")]
    public async Task Submit_parameter_in_authorize_url_cannot_override_deny(string key)
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var clientId = await _oauth.RegisterClientAsync();

        var result = await _oauth.AuthorizeAsync(_oauth.CreateBrowser(), clientId, userName, Password, deny: true,
            customize: p => p[key] = "accept");

        Assert.True(result.ConsentShown);
        Assert.Equal(Errors.AccessDenied, result.Error);
        Assert.Null(result.Code);
    }

    // Onay POST'u tek bir "submit=accept" ister; birden çok değer (ör. kurcalanmış form) reddedilir.
    [Fact]
    public async Task Consent_post_with_multiple_submit_values_is_denied()
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var clientId = await _oauth.RegisterClientAsync();

        var result = await _oauth.AuthorizeAsync(_oauth.CreateBrowser(), clientId, userName, Password, deny: true,
            extraConsentFields: [new("Submit", "accept")]);

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

        await _oauth.WithUserAsync(userName, async (users, user) =>
        {
            await users.RemoveFromRolesAsync(user, await users.GetRolesAsync(user));
            return await users.AddToRoleAsync(user, AppRoles.Sakin);
        });
        var response = await _oauth.RefreshAsync(tokens.ClientId, tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Errors.InvalidGrant, await ReadErrorAsync(response));
    }

    [Fact]
    public async Task Refresh_fails_after_staff_user_is_given_sakin_role()
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var tokens = await _oauth.GetTokensAsync(userName, Password);

        await _oauth.WithUserAsync(userName, (users, user) => users.AddToRoleAsync(user, AppRoles.Sakin));
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

        await _oauth.LockOutAsync(userName);
        var response = await _oauth.RefreshAsync(tokens.ClientId, tokens.RefreshToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Errors.InvalidGrant, await ReadErrorAsync(response));
    }

    // Şifre sıfırlanınca ya da değişince verilmiş refresh token'lar ölür; kullanıcı yeni şifresiyle yeniden bağlanır.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Refresh_fails_after_password_reset_or_change_until_user_reconnects(bool reset)
    {
        const string newPassword = "yeni12345";
        var userName = NewUserName("muhasebe");
        var otherName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        await _oauth.CreateUserAsync(otherName, Password, AppRoles.MuhasebeGorevli);
        var tokens = await _oauth.GetTokensAsync(userName, Password);
        var otherTokens = await _oauth.GetTokensAsync(otherName, Password);

        if (reset)
        {
            await _oauth.ResetPasswordAsync(userName, newPassword);
        }
        else
        {
            await _oauth.WithUserAsync(userName, (users, user) => users.ChangePasswordAsync(user, Password, newPassword));
        }

        var refused = await _oauth.RefreshAsync(tokens.ClientId, tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(Errors.InvalidGrant, await ReadErrorAsync(refused));

        // Şifresi değişmeyen kullanıcı etkilenmez.
        Assert.Equal(HttpStatusCode.OK, (await _oauth.RefreshAsync(otherTokens.ClientId, otherTokens.RefreshToken)).StatusCode);

        var reconnected = await _oauth.GetTokensAsync(userName, newPassword);
        Assert.Equal(HttpStatusCode.OK, (await _oauth.InitializeMcpAsync(reconnected.AccessToken)).StatusCode);
        var refreshed = await OAuthTestClient.ReadTokensAsync(
            reconnected.ClientId, await _oauth.RefreshAsync(reconnected.ClientId, reconnected.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, (await _oauth.InitializeMcpAsync(refreshed.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Code_exchange_fails_after_password_reset()
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var clientId = await _oauth.RegisterClientAsync();
        var authorization = await _oauth.AuthorizeAsync(_oauth.CreateBrowser(), clientId, userName, Password);
        Assert.NotNull(authorization.Code);

        await _oauth.ResetPasswordAsync(userName, "yeni12345");
        var response = await _oauth.ExchangeCodeAsync(clientId, authorization.Code!, authorization.CodeVerifier);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(Errors.InvalidGrant, await ReadErrorAsync(response));
    }

    // Çerez oturumu şifre sıfırlandıktan sonra bir süre daha açık kalır; onunla yeni bağlantı kurulamaz, yeniden giriş istenir.
    [Fact]
    public async Task Login_session_from_before_password_reset_cannot_authorize()
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var clientId = await _oauth.RegisterClientAsync();
        var browser = _oauth.CreateBrowser();
        Assert.NotNull((await _oauth.AuthorizeAsync(browser, clientId, userName, Password)).Code);

        await _oauth.ResetPasswordAsync(userName, "yeni12345");
        var (url, _) = OAuthTestClient.CreateAuthorizeRequest(clientId);
        var response = await browser.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", response.Headers.Location!.OriginalString);
        Assert.NotNull((await _oauth.AuthorizeAsync(browser, clientId, userName, "yeni12345")).Code);
    }

    // Güvenlik damgası yalnız şifreli token'ların içindedir: yanıtta düz metin olarak, id_token'da ya da başka yerde görünmez.
    [Fact]
    public async Task Security_stamp_is_carried_only_inside_encrypted_tokens()
    {
        var userName = NewUserName("muhasebe");
        var user = await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var clientId = await _oauth.RegisterClientAsync();
        var authorization = await _oauth.AuthorizeAsync(_oauth.CreateBrowser(), clientId, userName, Password);

        var response = await _oauth.ExchangeCodeAsync(clientId, authorization.Code!, authorization.CodeVerifier);
        var body = await response.Content.ReadAsStringAsync();
        var tokens = await OAuthTestClient.ReadTokensAsync(clientId, response);

        Assert.Equal(user.SecurityStamp, (await ValidateAccessTokenAsync(tokens.AccessToken)).GetClaim(OAuthConstants.SecurityStampClaim));
        Assert.DoesNotContain(user.SecurityStamp!, body);
        Assert.DoesNotContain("id_token", body);
        Assert.DoesNotContain(user.SecurityStamp!, authorization.Callback.ToString());
        foreach (var token in new[] { tokens.AccessToken, tokens.RefreshToken })
        {
            // JWE (beş parça): içerik şifreli; okunabilen tek parça başlıktır.
            var parts = token.Split('.');
            Assert.Equal(5, parts.Length);
            Assert.DoesNotContain(user.SecurityStamp!, Encoding.UTF8.GetString(Base64Url.DecodeFromChars(parts[0])));
        }
    }

    // Onay POST'u antiforgery ister: başka siteden gönderilen form kullanıcının adına izin veremez.
    [Fact]
    public async Task Consent_post_without_antiforgery_token_is_rejected()
    {
        var userName = NewUserName("muhasebe");
        var user = await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var clientId = await _oauth.RegisterClientAsync();
        var browser = _oauth.CreateBrowser();
        await OAuthTestClient.LoginAsync(browser, userName, Password);
        var (url, _) = OAuthTestClient.CreateAuthorizeRequest(clientId);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(url)).StatusCode);

        var form = QueryHelpers.ParseQuery(new Uri(new Uri("https://localhost"), url).Query)
            .ToDictionary(x => x.Key, x => x.Value.ToString());
        form["submit"] = "accept";
        var response = await browser.PostAsync("/connect/authorize", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        await using var scope = factory.Services.CreateAsyncScope();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        Assert.Empty(await authorizations.FindBySubjectAsync(user.Id).ToListAsync());
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

    // Secret'lı istemci (client_secret_post): secret olmadan ya da yanlış secret'la token yok; doğrusuyla /mcp ve yenileme çalışır.
    [Fact]
    public async Task Confidential_client_completes_the_flow_only_with_its_secret()
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var (clientId, secret) = await _oauth.RegisterClientAsync("Claude", "client_secret_post");
        Assert.False(string.IsNullOrEmpty(secret));
        var authorization = await _oauth.AuthorizeAsync(_oauth.CreateBrowser(), clientId, userName, Password);
        Assert.NotNull(authorization.Code);

        foreach (var wrong in new[] { null, "yanlis-secret" })
        {
            var refused = await _oauth.ExchangeCodeAsync(clientId, authorization.Code!, authorization.CodeVerifier, clientSecret: wrong);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            Assert.Equal(Errors.InvalidClient, await ReadErrorAsync(refused));
        }

        var tokens = await OAuthTestClient.ReadTokensAsync(clientId, await _oauth.ExchangeCodeAsync(
            clientId, authorization.Code!, authorization.CodeVerifier, clientSecret: secret));
        Assert.Equal(HttpStatusCode.OK, (await _oauth.InitializeMcpAsync(tokens.AccessToken)).StatusCode);

        Assert.False((await _oauth.RefreshAsync(clientId, tokens.RefreshToken)).IsSuccessStatusCode);
        var refreshed = await OAuthTestClient.ReadTokensAsync(
            clientId, await _oauth.RefreshAsync(clientId, tokens.RefreshToken, secret));
        Assert.Equal(HttpStatusCode.OK, (await _oauth.InitializeMcpAsync(refreshed.AccessToken)).StatusCode);
    }

    private async Task<ClaimsPrincipal> ValidateAccessTokenAsync(string accessToken)
    {
        using var scope = factory.Services.CreateScope();
        var validation = scope.ServiceProvider.GetRequiredService<OpenIddictValidationService>();
        return await validation.ValidateAccessTokenAsync(accessToken);
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("error").GetString();
    }
}
