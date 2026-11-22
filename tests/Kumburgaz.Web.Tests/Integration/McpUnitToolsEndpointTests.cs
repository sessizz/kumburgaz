using System.Text.Json;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Kumburgaz.Web.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class McpUnitToolsEndpointTests(KumburgazAppFactory factory)
{
    private const string Password = "test12345";

    private readonly OAuthTestClient _oauth = new(factory);

    [Fact]
    public async Task Unit_tools_are_listed_read_only_without_server_side_parameters()
    {
        await using var client = await ConnectAsync(AppRoles.SistemYonetici);

        var tools = (await client.ListToolsAsync()).ToDictionary(x => x.Name);

        foreach (var name in new[] { "search", "get_unit", "unit_statement", "overdue_dues" })
        {
            Assert.True(tools[name].ProtocolTool.Annotations?.ReadOnlyHint, name);
        }

        Assert.Equal(["query"], Parameters(tools["search"]));
        Assert.Equal(["unitId"], Parameters(tools["get_unit"]));
        Assert.Equal(["unitId", "from", "to"], Parameters(tools["unit_statement"]));
        Assert.Equal(["period", "blockId", "limit"], Parameters(tools["overdue_dues"]));
    }

    // Araç sonucu düz metin gider: Türkçe harfler Unicode kaçış dizisi olmadan görünmeli.
    [Fact]
    public async Task Search_and_get_unit_return_unescaped_turkish_letters_and_links()
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        var unitId = await SeedUnitAsync($"Şahin {token}");
        await using var client = await ConnectAsync(AppRoles.SistemYonetici);

        var search = await client.CallToolAsync("search", new Dictionary<string, object?> { ["query"] = $"sahin {token}" });
        var detail = await client.CallToolAsync("get_unit", new Dictionary<string, object?> { ["unitId"] = unitId });

        Assert.True(search.IsError is not true, Text(search));
        Assert.Contains($"Şahin {token}", Text(search));
        Assert.DoesNotContain('\\' + "u015", Text(search));
        Assert.Contains($"https://localhost/Units/Detail/{unitId}", Text(search));
        Assert.True(detail.IsError is not true, Text(detail));
        Assert.Contains($"Şahin {token}", Text(detail));
    }

    // Personel'in yetki matrisinde Daireler/Tahsilatlar yok (yalnız Panel ve Raporlar).
    [Fact]
    public async Task Search_rejects_user_without_unit_permissions_but_overdue_dues_works_with_raporlar()
    {
        await using var client = await ConnectAsync(AppRoles.Personel);

        var search = await client.CallToolAsync("search", new Dictionary<string, object?> { ["query"] = "sahin" });
        var overdue = await client.CallToolAsync("overdue_dues", new Dictionary<string, object?> { ["limit"] = 5 });

        Assert.True(search.IsError);
        Assert.Contains("yetkiniz yok", Text(search));
        Assert.True(overdue.IsError is not true, Text(overdue));
    }

    private async Task<int> SeedUnitAsync(string ownerName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var site = new Site { Name = $"Site {Guid.NewGuid():N}" };
        var block = new Block { Site = site, Name = "Z" };
        var unit = new Unit { Block = block, UnitNo = "901", OwnerName = ownerName };
        db.AddRange(site, block, unit);
        await db.SaveChangesAsync();
        return unit.Id;
    }

    private static string[] Parameters(McpClientTool tool)
        => tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(x => x.Name).ToArray();

    private static string Text(CallToolResult result)
        => string.Join('\n', result.Content.OfType<TextContentBlock>().Select(x => x.Text));

    private async Task<McpClient> ConnectAsync(string role)
    {
        var userName = $"mcp-{Guid.NewGuid():N}";
        await _oauth.CreateUserAsync(userName, Password, role);
        var tokens = await _oauth.GetTokensAsync(userName, Password);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("https://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {tokens.AccessToken}" }
            },
            _oauth.CreateBrowser(),
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }
}
