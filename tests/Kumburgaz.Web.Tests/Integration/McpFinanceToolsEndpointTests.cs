using System.Text.Json;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Kumburgaz.Web.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class McpFinanceToolsEndpointTests(KumburgazAppFactory factory)
{
    private const string Password = "test12345";

    private readonly OAuthTestClient _oauth = new(factory);

    [Fact]
    public async Task Finance_tools_are_listed_read_only()
    {
        await using var client = await ConnectAsync(AppRoles.SistemYonetici);

        var tools = (await client.ListToolsAsync()).ToDictionary(x => x.Name);

        foreach (var name in new[] { "site_summary", "collections", "cash_bank_accounts", "cash_bank_transactions", "ledger" })
        {
            Assert.True(tools[name].ProtocolTool.Annotations?.ReadOnlyHint, name);
        }

        Assert.Empty(Parameters(tools["site_summary"]));
        Assert.Equal(["from", "to", "unitId", "accountKey", "limit"], Parameters(tools["collections"]));
        Assert.Equal(["accountKey", "from", "to", "limit"], Parameters(tools["cash_bank_transactions"]));
        Assert.Equal(["kind", "categoryId", "from", "to", "limit"], Parameters(tools["ledger"]));
    }

    [Fact]
    public async Task Site_summary_returns_camel_case_figures_end_to_end()
    {
        await using var client = await ConnectAsync(AppRoles.SistemYonetici);

        var result = await client.CallToolAsync("site_summary", new Dictionary<string, object?>());

        Assert.True(result.IsError is not true, Text(result));
        using var json = JsonDocument.Parse(Text(result));
        foreach (var field in new[]
                 {
                     "cashTotal", "bankTotal", "accounts", "duesDebtTotal", "overdueDebtTotal",
                     "collectionsThisMonth", "expensesThisMonth", "incomeThisMonth"
                 })
        {
            Assert.True(json.RootElement.TryGetProperty(field, out _), field);
        }
    }

    [Fact]
    public async Task Bad_account_key_comes_back_as_turkish_tool_error()
    {
        await using var client = await ConnectAsync(AppRoles.SistemYonetici);

        var result = await client.CallToolAsync("cash_bank_transactions", new Dictionary<string, object?> { ["accountKey"] = "x" });

        Assert.True(result.IsError);
        Assert.Contains("Geçerli bir kasa/banka hesabı belirtin", Text(result));
    }

    // Personel yetki matrisinde yalnız Panel ve Raporlar var.
    [Fact]
    public async Task Personel_can_read_summary_but_not_ledger()
    {
        await using var client = await ConnectAsync(AppRoles.Personel);

        var summary = await client.CallToolAsync("site_summary", new Dictionary<string, object?>());
        var ledger = await client.CallToolAsync("ledger", new Dictionary<string, object?>());

        Assert.True(summary.IsError is not true, Text(summary));
        Assert.True(ledger.IsError);
        Assert.Contains("yetkiniz yok", Text(ledger));
    }

    private static string[] Parameters(McpClientTool tool)
        => tool.ProtocolTool.InputSchema.TryGetProperty("properties", out var props)
            ? props.EnumerateObject().Select(x => x.Name).ToArray()
            : [];

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
