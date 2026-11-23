using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kumburgaz.Web.Auth;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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
        Assert.False(root.TryGetProperty("client_secret_expires_at", out _));
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
        // RFC 7591: secret verilen yanıtta zorunlu; 0 = süresi dolmaz.
        Assert.Equal(0, root.GetProperty("client_secret_expires_at").GetInt64());
    }

    // client_secret_basic'te "+" OpenIddict tarafından boşluğa çevrilir; secret yalnız URL-güvenli karakter içermeli.
    [Fact]
    public async Task Confidential_client_secret_is_url_safe()
    {
        var client = CreateClient();
        for (var i = 0; i < 20; i++)
        {
            var response = await RegisterAsync(client, new
            {
                client_name = "Claude",
                redirect_uris = new[] { ClaudeCallback },
                token_endpoint_auth_method = "client_secret_basic"
            });
            var secret = (await ReadJsonAsync(response)).GetProperty("client_secret").GetString()!;
            Assert.Matches("^[A-Za-z0-9_-]+$", secret);
            Assert.True(secret.Length >= 43);
        }
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

    // Anonim uçtan veritabanına büyük satır yazdırılamaz.
    [Theory]
    [InlineData(5, 200, HttpStatusCode.Created)]
    [InlineData(6, 10, HttpStatusCode.BadRequest)]
    [InlineData(1, 201, HttpStatusCode.BadRequest)]
    public async Task Limits_redirect_uri_count_and_client_name_length(int uriCount, int nameLength, HttpStatusCode expected)
    {
        var name = new string('x', nameLength);
        var response = await RegisterAsync(CreateClient(), new
        {
            client_name = name,
            redirect_uris = Enumerable.Repeat(ClaudeCallback, uriCount)
        });

        Assert.Equal(expected, response.StatusCode);
        var root = await ReadJsonAsync(response);
        if (expected == HttpStatusCode.Created)
        {
            Assert.Equal(name, root.GetProperty("client_name").GetString());
        }
        else
        {
            Assert.Equal("invalid_client_metadata", root.GetProperty("error").GetString());
        }
    }

    // TestServer gövde sınırını uygulamaz; sınırın uca bağlı olduğu denetlenir (Kestrel uygular).
    [Fact]
    public void Registration_body_is_limited_to_16_kb()
    {
        var endpoint = Assert.Single(
            factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>(),
            x => x.RoutePattern.RawText?.Trim('/') == "connect/register");

        Assert.Equal(16384, endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize);
    }

    // IP sınırına ek olarak bütün adreslerin toplamı: çok IP'li bir saldırgan da tabloyu şişiremez.
    [Fact]
    public async Task Limits_registrations_globally_across_ips()
    {
        await using var limited = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton(new ClientRegistrationQuota(permitsPerHour: 2))));
        var client = limited.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var valid = new { client_name = "Claude", redirect_uris = new[] { ClaudeCallback } };
        var invalid = new { client_name = "Evil", redirect_uris = new[] { "https://evil.example/cb" } };

        // Reddedilen istek kotadan yemez.
        Assert.Equal(HttpStatusCode.BadRequest, (await RegisterAsync(client, invalid, "198.51.100.1")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(client, valid, "198.51.100.2")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(client, valid, "198.51.100.3")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await RegisterAsync(client, valid, "198.51.100.4")).StatusCode);
    }

    [Fact]
    public void Global_quota_is_300_registrations_per_hour()
    {
        using var services = new ServiceCollection().AddKumburgazOAuthRateLimits().BuildServiceProvider();
        var quota = services.GetRequiredService<ClientRegistrationQuota>();

        Assert.All(Enumerable.Range(0, 300), _ => Assert.True(quota.TryTake()));
        Assert.False(quota.TryTake());
        Assert.Same(quota, services.GetRequiredService<ClientRegistrationQuota>());
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
