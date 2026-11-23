using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Kumburgaz.Web.Auth;

// RFC 7591 dinamik istemci kaydı: claude.ai bağlayıcısı kendini burada kaydeder.
// Yalnız OAuthConstants.IsAllowedRedirectUri'nin kabul ettiği dönüş adresleri (Claude ve yerel/loopback) kaydedilir.
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[EnableRateLimiting(OAuthRateLimits.RegisterPolicy)]
public class ClientRegistrationController(
    IOpenIddictApplicationManager applications, ClientRegistrationQuota quota, IConfiguration config) : Controller
{
    // Uç anonimdir: gövde ve alan sınırları tek kaydın büyüklüğünü, hız sınırları (OAuthRateLimits) sayısını bağlar.
    private const int MaxBodyBytes = 16384;
    private const int MaxRedirectUris = 5;
    private const int MaxClientNameLength = 200;

    private const string AuthMethodNone = "none";
    private const string AuthMethodPost = "client_secret_post";
    private const string AuthMethodBasic = "client_secret_basic";

    [HttpPost("/connect/register")]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Register([FromBody] ClientRegistrationRequest? request, CancellationToken ct)
    {
        if (request?.RedirectUris is not { Count: > 0 } redirectUris)
            return Error("invalid_client_metadata", "redirect_uris zorunludur.");
        if (redirectUris.Count > MaxRedirectUris)
            return Error("invalid_client_metadata", $"redirect_uris en fazla {MaxRedirectUris} adres içerebilir.");

        var uris = new List<Uri>();
        foreach (var raw in redirectUris)
        {
            if (!OAuthConstants.IsAllowedRedirectUri(raw))
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
        if (clientName.Length > MaxClientNameLength)
            return Error("invalid_client_metadata", $"client_name en fazla {MaxClientNameLength} karakter olabilir.");

        if (!quota.TryTake())
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = "temporarily_unavailable", error_description = "Kayıt sınırına ulaşıldı; daha sonra yeniden deneyin." });

        var clientId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var secret = confidential ? System.Buffers.Text.Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)) : null;

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
        // İstemci yalnız /mcp kaynağı için token isteyebilir (RFC 8707 resource).
        foreach (var resource in config.GetAcceptedMcpResources())
            descriptor.Permissions.Add(Permissions.Prefixes.Resource + resource);

        await applications.CreateAsync(descriptor, ct);

        return StatusCode(StatusCodes.Status201Created, new ClientRegistrationResponse
        {
            ClientId = clientId,
            ClientIdIssuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ClientSecret = secret,
            ClientSecretExpiresAt = secret is null ? null : 0,
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
    // RFC 7591: secret verildiyse zorunlu; 0 = süresi dolmaz.
    [JsonPropertyName("client_secret_expires_at"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ClientSecretExpiresAt { get; set; }
    [JsonPropertyName("client_name")] public string ClientName { get; set; } = "";
    [JsonPropertyName("redirect_uris")] public IReadOnlyList<string?> RedirectUris { get; set; } = [];
    [JsonPropertyName("grant_types")] public string[] GrantTypes { get; set; } = ["authorization_code", "refresh_token"];
    [JsonPropertyName("response_types")] public string[] ResponseTypes { get; set; } = ["code"];
    [JsonPropertyName("token_endpoint_auth_method")] public string TokenEndpointAuthMethod { get; set; } = AuthMethodDefault;
    private const string AuthMethodDefault = "none";
}
