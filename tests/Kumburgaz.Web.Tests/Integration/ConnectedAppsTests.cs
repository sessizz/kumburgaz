using System.Net;
using System.Text.RegularExpressions;
using Kumburgaz.Web.Models;
using Xunit;

namespace Kumburgaz.Web.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class ConnectedAppsTests(KumburgazAppFactory factory)
{
    private const string Password = "test12345";

    private readonly OAuthTestClient _oauth = new(factory);

    private static string NewUserName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private async Task<HttpClient> LoginBrowserAsync(string userName)
    {
        var browser = _oauth.CreateBrowser();
        await OAuthTestClient.LoginAsync(browser, userName, Password);
        return browser;
    }

    // Bağlı uygulamalar sayfasından iptal formunu okur: (işlem adresi, antiforgery token).
    private static (string Action, string Token) ReadRevokeForm(string html)
    {
        var action = Regex.Match(html, "action=\"(/ConnectedApps/Revoke/[^\"]+)\"").Groups[1].Value;
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(action), "İptal formu bulunamadı.");
        Assert.False(string.IsNullOrEmpty(token), "Antiforgery token bulunamadı.");
        return (WebUtility.HtmlDecode(action), WebUtility.HtmlDecode(token));
    }

    private static async Task<string> GetIndexHtmlAsync(HttpClient browser)
    {
        var response = await browser.GetAsync("/ConnectedApps");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static Task<HttpResponseMessage> PostRevokeAsync(HttpClient browser, string action, string token)
        => browser.PostAsync(action, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        }));

    [Fact]
    public async Task Revoked_connection_cannot_refresh()
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var tokens = await _oauth.GetTokensAsync(userName, Password);
        var browser = await LoginBrowserAsync(userName);

        var (action, token) = ReadRevokeForm(await GetIndexHtmlAsync(browser));
        var revoke = await PostRevokeAsync(browser, action, token);
        Assert.Equal(HttpStatusCode.Redirect, revoke.StatusCode);

        var refresh = await _oauth.RefreshAsync(tokens.ClientId, tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
    }

    [Fact]
    public async Task User_cannot_revoke_someone_elses_connection()
    {
        var ownerName = NewUserName("muhasebe");
        var otherName = NewUserName("personel");
        await _oauth.CreateUserAsync(ownerName, Password, AppRoles.MuhasebeGorevli);
        await _oauth.CreateUserAsync(otherName, Password, AppRoles.Personel);
        var ownerTokens = await _oauth.GetTokensAsync(ownerName, Password);
        await _oauth.GetTokensAsync(otherName, Password);

        var ownerBrowser = await LoginBrowserAsync(ownerName);
        var (ownerAction, _) = ReadRevokeForm(await GetIndexHtmlAsync(ownerBrowser));

        var otherBrowser = await LoginBrowserAsync(otherName);
        var (_, otherToken) = ReadRevokeForm(await GetIndexHtmlAsync(otherBrowser));

        var response = await PostRevokeAsync(otherBrowser, ownerAction, otherToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Başkasının bağlantısı etkilenmedi.
        var refresh = await _oauth.RefreshAsync(ownerTokens.ClientId, ownerTokens.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Fact]
    public async Task Index_lists_own_connection()
    {
        var userName = NewUserName("muhasebe");
        await _oauth.CreateUserAsync(userName, Password, AppRoles.MuhasebeGorevli);
        var clientName = $"Baglayici-{Guid.NewGuid():N}";
        var clientId = await _oauth.RegisterClientAsync(clientName);
        var authorization = await _oauth.AuthorizeAsync(_oauth.CreateBrowser(), clientId, userName, Password);
        Assert.NotNull(authorization.Code);

        var browser = await LoginBrowserAsync(userName);
        var html = await GetIndexHtmlAsync(browser);

        Assert.Contains(clientName, html);
        Assert.Contains("Aktif", html);
    }
}
