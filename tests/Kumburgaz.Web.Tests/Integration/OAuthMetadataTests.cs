using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Kumburgaz.Web.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class OAuthMetadataTests(KumburgazAppFactory factory)
{
    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });

    // claude.ai bağlayıcısı sunucuyu RFC 8414 metadata'sından keşfeder: code + PKCE akışı,
    // mcp kapsamı ve dinamik istemci kaydı (DCR) adresi burada ilan edilmeli.
    [Fact]
    public async Task Authorization_server_metadata_advertises_code_flow_pkce_and_registration()
    {
        var response = await CreateClient().GetAsync("/.well-known/oauth-authorization-server");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(KumburgazAppFactory.Issuer, root.GetProperty("issuer").GetString());
        Assert.EndsWith("/connect/authorize", root.GetProperty("authorization_endpoint").GetString());
        Assert.EndsWith("/connect/token", root.GetProperty("token_endpoint").GetString());
        Assert.EndsWith("/connect/register", root.GetProperty("registration_endpoint").GetString());
        // PKCE yalnız S256: "plain" doğrulayıcıyı korumaz.
        Assert.Equal(["S256"], Strings(root.GetProperty("code_challenge_methods_supported")));
        Assert.Contains("mcp", Strings(root.GetProperty("scopes_supported")));
    }

    private static IEnumerable<string?> Strings(JsonElement array)
        => array.EnumerateArray().Select(x => x.GetString());
}
