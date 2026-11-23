using System.Collections.Immutable;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kumburgaz.Web.Auth;

// claude.ai bağlayıcısının OAuth uçları (OpenIddict passthrough): giriş, onay ve token.
// İstek parametrelerini (istemci, dönüş adresi, PKCE, resource) OpenIddict bu action'lardan önce doğrular.
// Sakin kullanıcı /m'e yönlendirilmez; istemciye access_denied döner (aksi halde Claude'un akışı yarıda kalır).
[SkipSakinRestriction]
public class AuthorizationController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IOpenIddictApplicationManager applications,
    IOpenIddictAuthorizationManager authorizations,
    OAuthPrincipalFactory principals) : Controller
{
    private const string IneligibleMessage = "Bu hesap bağlayıcıyı kullanamaz.";
    private const string DeniedMessage = "Kullanıcı bağlantıya izin vermedi.";

    // Bağlayıcı her zaman mcp kapsamını alır; offline_access olmadan OpenIddict refresh token vermez.
    private static readonly ImmutableArray<string> GrantedScopes = [OAuthConstants.Scope, Scopes.OfflineAccess];

    [AllowAnonymous]
    [HttpGet("~/connect/authorize")]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OAuth yetkilendirme isteği okunamadı.");

        var user = await GetSignedInUserAsync();
        if (user is null)
        {
            return ChallengeLogin();
        }

        var principal = await principals.CreateAsync(user, GrantedScopes, request.GetResources().FirstOrDefault());
        if (principal is null)
        {
            return ForbidWithError(Errors.AccessDenied, IneligibleMessage);
        }

        var application = await FindApplicationAsync(request);
        var authorization = await FindPermanentAuthorizationAsync(user, application);
        if (authorization is not null)
        {
            principal.SetAuthorizationId(await authorizations.GetIdAsync(authorization));
            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var clientName = await applications.GetLocalizedDisplayNameAsync(application) ?? request.ClientId!;
        var parameters = Request.Query
            .Where(p => !IsConsentFormField(p.Key))
            .SelectMany(p => p.Value.Select(value => KeyValuePair.Create(p.Key, value)))
            .ToList();
        return View("Consent", new ConsentViewModel(clientName, user.UserName ?? "", parameters));
    }

    [HttpPost("~/connect/authorize")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Consent()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OAuth yetkilendirme isteği okunamadı.");

        // Yalnız tek bir "submit=accept" onaydır; eksik, farklı ya da birden çok değer (kurcalanmış form) reddir.
        if (Request.Form["submit"] is not ["accept"])
        {
            return ForbidWithError(Errors.AccessDenied, DeniedMessage);
        }

        var user = await GetSignedInUserAsync();
        if (user is null)
        {
            return ChallengeLogin();
        }

        // Onay ekranı açıkken rolü düşen ya da kilitlenen kullanıcı da reddedilir.
        var principal = await principals.CreateAsync(user, GrantedScopes, request.GetResources().FirstOrDefault());
        if (principal is null)
        {
            return ForbidWithError(Errors.AccessDenied, IneligibleMessage);
        }

        var application = await FindApplicationAsync(request);
        var authorization = await FindPermanentAuthorizationAsync(user, application)
            ?? await authorizations.CreateAsync(
                principal: principal,
                subject: user.Id,
                client: (await applications.GetIdAsync(application))!,
                type: AuthorizationTypes.Permanent,
                scopes: principal.GetScopes());

        principal.SetAuthorizationId(await authorizations.GetIdAsync(authorization));
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("~/connect/token")]
    [Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OAuth token isteği okunamadı.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
        {
            throw new InvalidOperationException($"Desteklenmeyen grant türü: {request.GrantType}");
        }

        // Code/refresh token'daki kimlik; kullanıcı ve rolleri her seferinde veritabanından yeniden okunur.
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var userId = result.Principal?.GetClaim(Claims.Subject);
        var user = userId is null ? null : await users.FindByIdAsync(userId);
        var principal = user is not null && await principals.IsEligibleAsync(user, result.Principal!)
            ? await principals.CreateAsync(user, result.Principal!.GetScopes(), request.GetResources().FirstOrDefault())
            : null;
        if (principal is null)
        {
            return ForbidWithError(Errors.InvalidGrant, IneligibleMessage);
        }

        // OpenIddict authorization id'yi code/refresh token'dan yeni token'lara taşır; "Bağlı uygulamalar"dan
        // yapılan iptal yenilenen token'ları da kapsar.
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // Çerez, şifre değiştikten sonra en çok 30 dakika daha geçerlidir (SecurityStampValidator). Burada hemen denetlenir:
    // eski oturum yeni damgalı bir bağlantı kuramasın.
    private async Task<ApplicationUser?> GetSignedInUserAsync()
    {
        var result = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        return result.Succeeded ? await signIn.ValidateSecurityStampAsync(result.Principal) : null;
    }

    // Mevcut giriş ekranına gider ve aynı yetkilendirme isteğiyle geri döner.
    private ChallengeResult ChallengeLogin()
    {
        var parameters = (Request.HasFormContentType ? Request.Form.AsEnumerable() : Request.Query)
            .Where(p => !IsConsentFormField(p.Key))
            .ToList();

        return Challenge(
            new AuthenticationProperties { RedirectUri = Request.PathBase + Request.Path + QueryString.Create(parameters) },
            IdentityConstants.ApplicationScheme);
    }

    // Onay formunun kendi alanları OAuth parametresi olarak taşınmaz: authorize adresine eklenen
    // "submit=accept" gizli alana dönüşüp kullanıcının "Reddet" seçimini ezmesin.
    // Sorgu/form anahtarları büyük-küçük harf duyarsızdır ("Submit" de aynı alandır).
    private static bool IsConsentFormField(string key)
        => string.Equals(key, "submit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "__RequestVerificationToken", StringComparison.OrdinalIgnoreCase);

    // OpenIddict hatayı authorize'da istemcinin dönüş adresine, token'da 400 JSON olarak yazar.
    private ForbidResult ForbidWithError(string error, string description)
        => Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
            }),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    private async Task<object> FindApplicationAsync(OpenIddictRequest request)
        => await applications.FindByClientIdAsync(request.ClientId!)
            ?? throw new InvalidOperationException("İstemci bulunamadı.");

    private async Task<object?> FindPermanentAuthorizationAsync(ApplicationUser user, object application)
    {
        var applicationId = await applications.GetIdAsync(application);
        await foreach (var authorization in authorizations.FindAsync(
                           subject: user.Id,
                           client: applicationId,
                           status: Statuses.Valid,
                           type: AuthorizationTypes.Permanent,
                           scopes: GrantedScopes))
        {
            return authorization;
        }

        return null;
    }
}

// Parameters: onay POST'unda OpenIddict'in yeniden okuyup doğrulayacağı yetkilendirme isteği.
public sealed record ConsentViewModel(
    string ClientName, string UserName, IReadOnlyList<KeyValuePair<string, string?>> Parameters);
