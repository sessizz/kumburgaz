using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kumburgaz.Web.Auth;

// RFC 7591 dinamik istemci kaydı: claude.ai bağlayıcısı kendini burada kaydeder.
// Yalnız OAuthConstants.AllowedRedirectUris içindeki dönüş adresleri kabul edilir.
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[EnableRateLimiting(OAuthRateLimits.RegisterPolicy)]
public class ClientRegistrationController(IOpenIddictApplicationManager applications) : Controller
{
    private const string AuthMethodNone = "none";
    private const string AuthMethodPost = "client_secret_post";
    private const string AuthMethodBasic = "client_secret_basic";

    [HttpPost("/connect/register")]
    public async Task<IActionResult> Register([FromBody] ClientRegistrationRequest? request, CancellationToken ct)
    {
        if (request?.RedirectUris is not { Count: > 0 } redirectUris)
            return Error("invalid_client_metadata", "redirect_uris zorunludur.");

        var uris = new List<Uri>();
        foreach (var raw in redirectUris)
        {
            if (raw is null || !OAuthConstants.AllowedRedirectUris.Contains(raw))
                return Error("invalid_redirect_uri", "Dönüş adresi izinli adresler arasında değil.");
            uris.Add(new Uri(raw, UriKind.Absolute));
        }

        var authMethod = string.IsNullOrWhiteSpace(request.TokenEndpointAuthMethod)
            ? AuthMethodNone
            : request.TokenEndpointAuthMethod;
        if (authMethod is not (AuthMethodNone or AuthMethodPost or AuthMethodBasic))
            return Error("invalid_client_metadata", "token_endpoint_auth_method desteklenmiyor.");

        var confidential = authMethod != AuthMethodNone;
        var clientName = string.IsNullOrWhiteSpace(request.ClientName) ? "Claude" : request.ClientName.Trim();
        if (clientName.Length > 100) clientName = clientName[..100];

        var clientId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var secret = confidential ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) : null;

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = secret,
            ClientType = confidential ? ClientTypes.Confidential : ClientTypes.Public,
            ConsentType = ConsentTypes.Explicit,
            DisplayName = clientName,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Prefixes.Scope + OAuthConstants.Scope
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange }
        };
        foreach (var uri in uris) descriptor.RedirectUris.Add(uri);

        await applications.CreateAsync(descriptor, ct);

        return StatusCode(StatusCodes.Status201Created, new ClientRegistrationResponse
        {
            ClientId = clientId,
            ClientIdIssuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ClientSecret = secret,
            ClientName = clientName,
            RedirectUris = redirectUris,
            TokenEndpointAuthMethod = authMethod
        });
    }

    private BadRequestObjectResult Error(string error, string description)
        => BadRequest(new { error, error_description = description });
}

public class ClientRegistrationRequest
{
    [JsonPropertyName("client_name")] public string? ClientName { get; set; }
    [JsonPropertyName("redirect_uris")] public List<string?>? RedirectUris { get; set; }
    [JsonPropertyName("grant_types")] public List<string>? GrantTypes { get; set; }
    [JsonPropertyName("response_types")] public List<string>? ResponseTypes { get; set; }
    [JsonPropertyName("token_endpoint_auth_method")] public string? TokenEndpointAuthMethod { get; set; }
}

public class ClientRegistrationResponse
{
    [JsonPropertyName("client_id")] public string ClientId { get; set; } = "";
    [JsonPropertyName("client_id_issued_at")] public long ClientIdIssuedAt { get; set; }
    [JsonPropertyName("client_secret"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientSecret { get; set; }
    [JsonPropertyName("client_name")] public string ClientName { get; set; } = "";
    [JsonPropertyName("redirect_uris")] public IReadOnlyList<string?> RedirectUris { get; set; } = [];
    [JsonPropertyName("grant_types")] public string[] GrantTypes { get; set; } = ["authorization_code", "refresh_token"];
    [JsonPropertyName("response_types")] public string[] ResponseTypes { get; set; } = ["code"];
    [JsonPropertyName("token_endpoint_auth_method")] public string TokenEndpointAuthMethod { get; set; } = AuthMethodDefault;
    private const string AuthMethodDefault = "none";
}
