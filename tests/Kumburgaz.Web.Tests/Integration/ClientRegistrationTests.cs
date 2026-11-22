using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Kumburgaz.Web.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class ClientRegistrationTests(KumburgazAppFactory factory)
{
    private const string ClaudeCallback = "https://claude.ai/api/mcp/auth_callback";

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });

    // Hız sınırı CF-Connecting-IP başına tutulur; paylaşılan fixture'da her istek ayrı IP'den gelir.
    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, object body, string? ip = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connect/register")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("CF-Connecting-IP", ip ?? $"10.{Random.Shared.Next(256)}.{Random.Shared.Next(256)}.{Guid.NewGuid().GetHashCode() & 0xFF}");
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    [Fact]
    public async Task Registers_public_client_for_claude_callback()
    {
        var response = await RegisterAsync(CreateClient(), new
        {
            client_name = "Claude",
            redirect_uris = new[] { ClaudeCallback },
            grant_types = new[] { "authorization_code", "refresh_token" },
            response_types = new[] { "code" },
            token_endpoint_auth_method = "none"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var root = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("client_id").GetString()));
        Assert.Equal("none", root.GetProperty("token_endpoint_auth_method").GetString());
        Assert.False(root.TryGetProperty("client_secret", out _));
        Assert.Equal(ClaudeCallback, root.GetProperty("redirect_uris")[0].GetString());
    }

    [Fact]
    public async Task Registers_confidential_client_when_secret_auth_requested()
    {
        var response = await RegisterAsync(CreateClient(), new
        {
            client_name = "Claude",
            redirect_uris = new[] { ClaudeCallback },
            token_endpoint_auth_method = "client_secret_post"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var root = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("client_secret").GetString()));
        Assert.Equal("client_secret_post", root.GetProperty("token_endpoint_auth_method").GetString());
    }

    [Fact]
    public async Task Rejects_redirect_uri_outside_allowlist()
    {
        var response = await RegisterAsync(CreateClient(), new
        {
            client_name = "Evil",
            redirect_uris = new[] { "https://evil.example/cb" }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_redirect_uri", (await ReadJsonAsync(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Rejects_missing_redirect_uris()
    {
        var response = await RegisterAsync(CreateClient(), new { client_name = "Claude" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_client_metadata", (await ReadJsonAsync(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Limits_registrations_per_ip()
    {
        var client = CreateClient();
        var ip = $"203.0.113.{Random.Shared.Next(1, 255)}";
        var body = new { client_name = "Claude", redirect_uris = new[] { ClaudeCallback } };

        for (var i = 0; i < 20; i++)
            Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(client, body, ip)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await RegisterAsync(client, body, ip)).StatusCode);
    }
}
