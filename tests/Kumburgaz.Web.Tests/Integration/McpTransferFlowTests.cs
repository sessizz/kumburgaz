using System.Text.Json;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Kumburgaz.Web.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class McpTransferFlowTests(KumburgazAppFactory factory)
{
    private const string Password = "test12345";

    private readonly OAuthTestClient _oauth = new(factory);

    [Fact]
    public async Task Transfer_via_mcp_shows_in_both_accounts()
    {
        var seed = await SeedAsync();
        await using var client = await ConnectAsync(AppRoles.MuhasebeGorevli);
        var key = Guid.NewGuid().ToString();
        var input = new Dictionary<string, object?>
        {
            ["fromAccountKey"] = $"cash:{seed.CashId}", ["toAccountKey"] = $"bank:{seed.BankId}", ["amount"] = "1.250,50",
            ["date"] = "2026-03-05", ["description"] = "ATM"
        };

        var preview = await CallAsync(client, "record_transfer", input);
        Assert.False(preview.GetProperty("willWrite").GetBoolean());
        Assert.Equal($"cash:{seed.CashId}", preview.GetProperty("from").GetProperty("key").GetString());
        Assert.Equal(seed.CashName, preview.GetProperty("from").GetProperty("name").GetString());
        Assert.Equal($"bank:{seed.BankId}", preview.GetProperty("to").GetProperty("key").GetString());
        Assert.Equal(1_250.50m, preview.GetProperty("amount").GetDecimal());
        Assert.Equal("2026-03-05", preview.GetProperty("date").GetString());
        Assert.Equal("Bankaya yatır: ATM", preview.GetProperty("description").GetString());
        Assert.Equal(0, await CountLegsAsync(seed));

        var confirmed = new Dictionary<string, object?>(input)
        {
            ["confirm"] = true, ["preview_token"] = preview.GetProperty("previewToken").GetString(), ["idempotency_key"] = key
        };
        var created = await CallAsync(client, "record_transfer", confirmed);
        var retry = await CallAsync(client, "record_transfer", confirmed);

        Assert.True(created.GetProperty("created").GetBoolean());
        Assert.Equal(created.GetRawText(), retry.GetRawText());
        Assert.Equal($"https://localhost/CashBank/CashBox/{seed.CashId}", created.GetProperty("links").GetProperty("from").GetString());
        Assert.Equal($"https://localhost/CashBank/Bank/{seed.BankId}", created.GetProperty("links").GetProperty("to").GetString());
        Assert.Equal(2, await CountLegsAsync(seed));

        var cash = await CallAsync(client, "cash_bank_transactions", new() { ["accountKey"] = $"cash:{seed.CashId}" });
        var bank = await CallAsync(client, "cash_bank_transactions", new() { ["accountKey"] = $"bank:{seed.BankId}" });
        var cashRow = Assert.Single(cash.GetProperty("rows").EnumerateArray());
        var bankRow = Assert.Single(bank.GetProperty("rows").EnumerateArray());
        Assert.Equal("Bankaya yatır: ATM", cashRow.GetProperty("description").GetString());
        Assert.Equal(1_250.50m, cashRow.GetProperty("outflow").GetDecimal());
        Assert.Equal("Bankaya yatır: ATM", bankRow.GetProperty("description").GetString());
        Assert.Equal(1_250.50m, bankRow.GetProperty("inflow").GetDecimal());
    }

    [Fact]
    public async Task Read_only_role_cannot_record_a_transfer()
    {
        var seed = await SeedAsync();
        await using var client = await ConnectAsync(AppRoles.SadeceGoruntuleme);

        var result = await client.CallToolAsync("record_transfer", new Dictionary<string, object?>
        {
            ["fromAccountKey"] = $"cash:{seed.CashId}", ["toAccountKey"] = $"bank:{seed.BankId}", ["amount"] = 10
        });

        Assert.True(result.IsError);
        Assert.Contains("yetkiniz yok", Text(result));
        Assert.Equal(0, await CountLegsAsync(seed));
    }

    private sealed record Seed(int CashId, string CashName, int BankId);

    private async Task<Seed> SeedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = Guid.NewGuid().ToString("N")[..10];
        var cash = new CashBox { Name = $"Kasa {id}", OpeningBalanceDate = Utc(2025, 1, 1) };
        var bank = new BankAccount { Name = $"Banka {id}", OpeningBalanceDate = Utc(2025, 1, 1) };
        db.AddRange(cash, bank);
        await db.SaveChangesAsync();
        return new Seed(cash.Id, cash.Name, bank.Id);
    }

    private async Task<int> CountLegsAsync(Seed seed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().LedgerTransactions
            .CountAsync(x => x.CashBoxId == seed.CashId || x.BankAccountId == seed.BankId);
    }

    private static async Task<JsonElement> CallAsync(McpClient client, string tool, Dictionary<string, object?> input)
    {
        var result = await client.CallToolAsync(tool, input);
        Assert.True(result.IsError is not true, Text(result));
        return JsonDocument.Parse(Text(result)).RootElement;
    }

    private static string Text(CallToolResult result)
        => string.Join('\n', result.Content.OfType<TextContentBlock>().Select(x => x.Text));

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

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
