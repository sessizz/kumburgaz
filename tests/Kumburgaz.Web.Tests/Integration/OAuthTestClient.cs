using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kumburgaz.Web.Tests.Integration;

public sealed record TokenResult(string ClientId, string AccessToken, string RefreshToken, int ExpiresIn);

// Yetkilendirme isteğinin sonucu: istemcinin dönüş adresine gelen code ya da error.
// Onay ekranı gösterildiyse HTML'i ConsentHtml'dedir.
public sealed record AuthorizationResult(Uri Callback, string CodeVerifier, string? ConsentHtml)
{
    public bool ConsentShown => ConsentHtml is not null;
    public string? Code => QueryValue("code");
    public string? Error => QueryValue("error");

    private string? QueryValue(string key)
        => QueryHelpers.ParseQuery(Callback.Query).TryGetValue(key, out var value) ? value.ToString() : null;
}

/// <summary>
/// claude.ai bağlayıcısının OAuth akışını taklit eder: DCR → giriş → authorize (PKCE S256) → onay → token.
/// Her "tarayıcı" (HttpClient) kendi çerez kabını taşır; yönlendirmeler elle izlenir.
/// </summary>
public sealed class OAuthTestClient(KumburgazAppFactory factory)
{
    public const string RedirectUri = "https://claude.ai/api/mcp/auth_callback";

    private static int _ipCounter;

    public HttpClient CreateBrowser() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });

    public async Task<ApplicationUser> CreateUserAsync(string userName, string password, params string[] roles)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = $"{userName}@test.local",
            EmailConfirmed = true,
            FullName = $"{userName} Test"
        };
        AssertSucceeded(await users.CreateAsync(user, password));
        if (roles.Length > 0)
        {
            AssertSucceeded(await users.AddToRolesAsync(user, roles));
        }

        return user;
    }

    public async Task<TokenResult> GetTokensAsync(string userName, string password, string? resource = null)
    {
        var clientId = await RegisterClientAsync();
        var authorization = await AuthorizeAsync(CreateBrowser(), clientId, userName, password, resource);
        Assert.True(authorization.Code is not null, $"Yetki kodu dönmedi: {authorization.Callback}");

        var response = await ExchangeCodeAsync(clientId, authorization.Code!, authorization.CodeVerifier, resource);
        return await ReadTokensAsync(clientId, response);
    }

    public async Task<HttpResponseMessage> RefreshAsync(string clientId, string refreshToken)
        => await CreateBrowser().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken
        }));

    // Hız sınırı CF-Connecting-IP başına tutulur; her kayıt ayrı bir IP'den gelir.
    public async Task<string> RegisterClientAsync(string clientName = "Claude")
    {
        var n = Interlocked.Increment(ref _ipCounter);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/register")
        {
            Content = JsonContent.Create(new
            {
                client_name = clientName,
                redirect_uris = new[] { RedirectUri },
                token_endpoint_auth_method = "none"
            })
        };
        request.Headers.Add("CF-Connecting-IP", $"172.16.{(n >> 8) & 0xFF}.{n & 0xFF}");

        var response = await CreateBrowser().SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("client_id").GetString()!;
    }

    public static (string Url, string CodeVerifier) CreateAuthorizeRequest(
        string clientId, string? resource = null, Action<Dictionary<string, string?>>? customize = null)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var parameters = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = RedirectUri,
            ["scope"] = "mcp",
            ["state"] = Guid.NewGuid().ToString("N"),
            ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256",
            ["resource"] = resource
        };
        customize?.Invoke(parameters);

        var url = QueryHelpers.AddQueryString("/connect/authorize",
            parameters.Where(p => p.Value is not null).Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)));
        return (url, verifier);
    }

    // Tarayıcıyı authorize isteğinden istemcinin dönüş adresine kadar götürür:
    // gerekirse giriş yapar, onay ekranı çıkarsa izin verir (deny: true ise reddeder).
    // extraConsentFields: onay POST'una tıklanan düğmeden önce eklenen alanlar (kurcalanmış form).
    public async Task<AuthorizationResult> AuthorizeAsync(
        HttpClient browser, string clientId, string userName, string password, string? resource = null, bool deny = false,
        Action<Dictionary<string, string?>>? customize = null,
        IEnumerable<KeyValuePair<string, string>>? extraConsentFields = null)
    {
        var (url, verifier) = CreateAuthorizeRequest(clientId, resource, customize);
        string? consentHtml = null;
        var response = await browser.GetAsync(url);

        for (var step = 0; step < 10; step++)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var html = await response.Content.ReadAsStringAsync();
                Assert.Contains("name=\"submit\"", html);
                consentHtml = html;

                var form = ReadHiddenInputs(html);
                form.AddRange(extraConsentFields ?? []);
                form.Add(new("submit", deny ? "deny" : "accept"));
                var action = Attribute(Regex.Match(html, "<form\\b[^>]*>", RegexOptions.IgnoreCase).Value, "action");
                Assert.Equal("/connect/authorize", action);
                response = await browser.PostAsync(action, new FormUrlEncodedContent(form));
                continue;
            }

            Assert.True(response.StatusCode == HttpStatusCode.Redirect,
                $"Beklenmeyen yanıt {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            var location = response.Headers.Location!;
            if (location.IsAbsoluteUri && location.GetLeftPart(UriPartial.Path) == RedirectUri)
            {
                return new AuthorizationResult(location, verifier, consentHtml);
            }

            var path = location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
            response = path.StartsWith("/Identity/Account/Login", StringComparison.OrdinalIgnoreCase)
                ? await LoginAsync(browser, path, userName, password)
                : await browser.GetAsync(path);
        }

        throw new InvalidOperationException("Yetkilendirme akışı dönüş adresine ulaşmadı.");
    }

    public async Task<HttpResponseMessage> ExchangeCodeAsync(string clientId, string code, string? codeVerifier, string? resource = null)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["redirect_uri"] = RedirectUri
        };
        if (codeVerifier is not null) form["code_verifier"] = codeVerifier;
        if (resource is not null) form["resource"] = resource;

        return await CreateBrowser().PostAsync("/connect/token", new FormUrlEncodedContent(form));
    }

    public static async Task<TokenResult> ReadTokensAsync(string clientId, HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Token yanıtı {(int)response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        return new TokenResult(
            clientId,
            root.GetProperty("access_token").GetString()!,
            root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString()! : "",
            root.GetProperty("expires_in").GetInt32());
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient browser, string loginPath, string userName, string password)
    {
        var page = await browser.GetAsync(loginPath);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var form = ReadHiddenInputs(await page.Content.ReadAsStringAsync());
        form.Add(new("Input.UserNameOrEmail", userName));
        form.Add(new("Input.Password", password));

        var response = await browser.PostAsync(loginPath, new FormUrlEncodedContent(form));
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, $"Giriş başarısız: {userName}");
        return response;
    }

    // Formdaki gizli alanlar (antiforgery token, ReturnUrl, OAuth parametreleri).
    private static List<KeyValuePair<string, string>> ReadHiddenInputs(string html)
    {
        var fields = new List<KeyValuePair<string, string>>();
        foreach (Match input in Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var tag = input.Value;
            if (!string.Equals(Attribute(tag, "type"), "hidden", StringComparison.OrdinalIgnoreCase)) continue;
            var name = Attribute(tag, "name");
            if (name is null) continue;
            fields.Add(new(name, Attribute(tag, "value") ?? ""));
        }

        return fields;
    }

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $"\\s{name}=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    private static string Base64Url(byte[] bytes) => System.Buffers.Text.Base64Url.EncodeToString(bytes);

    private static void AssertSucceeded(IdentityResult result)
        => Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
}
