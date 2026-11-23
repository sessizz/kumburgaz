using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Kumburgaz.Web.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class CashBankReorderEndpointTests(KumburgazAppFactory factory)
{
    private const string Password = "test12345";
    private const string DateText = "2026-11-23";
    private static readonly DateTime Day = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
    private readonly OAuthTestClient _oauth = new(factory);

    [Fact]
    public async Task ReorderDay_requires_kasabanka_write()
    {
        var (cashId, firstId, secondId) = await SeedMovementsAsync();
        var userName = await CreateUserAsync(AppRoles.Personel);
        using var browser = await LoginAsync(userName);
        var token = await ReadTokenAsync(browser, "/");

        using var response = await ReorderAsync(browser, "cash", cashId, DateText, token,
            $"ledger:{secondId}", $"ledger:{firstId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertDatesAsync(firstId, secondId, Day.AddTicks(80), Day.AddTicks(90));
    }

    [Fact]
    public async Task ReorderDay_without_antiforgery_is_rejected()
    {
        var (cashId, firstId, secondId) = await SeedMovementsAsync();
        using var browser = await LoginAsync(await CreateUserAsync(AppRoles.SistemYonetici));

        using var response = await ReorderAsync(browser, "cash", cashId, DateText, null,
            $"ledger:{secondId}", $"ledger:{firstId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertDatesAsync(firstId, secondId, Day.AddTicks(80), Day.AddTicks(90));
    }

    [Theory]
    [InlineData("other", DateText, "valid")]
    [InlineData("cash", "23.11.2026", "valid")]
    [InlineData("cash", DateText, "duplicate")]
    [InlineData("cash", DateText, "malformed")]
    public async Task ReorderDay_returns_a_json_error_for_invalid_input(string kind, string day, string rows)
    {
        var (cashId, firstId, secondId) = await SeedMovementsAsync();
        using var browser = await LoginAsync(await CreateUserAsync(AppRoles.SistemYonetici));
        var token = await ReadTokenAsync(browser, $"/CashBank/CashBox/{cashId}");
        var lastRow = rows switch
        {
            "duplicate" => $"ledger:{firstId}",
            "malformed" => "ledger:none",
            _ => $"ledger:{secondId}"
        };

        using var response = await ReorderAsync(browser, kind, cashId, day, token, $"ledger:{firstId}", lastRow);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("error").GetString()));
        await AssertDatesAsync(firstId, secondId, Day.AddTicks(80), Day.AddTicks(90));
    }

    [Fact]
    public async Task ReorderDay_persists_order_used_by_excel_and_mcp()
    {
        var (cashId, firstId, secondId) = await SeedMovementsAsync();
        var userName = await CreateUserAsync(AppRoles.SistemYonetici);
        using var browser = await LoginAsync(userName);
        var detailUrl = $"/CashBank/CashBox/{cashId}";
        var token = await ReadTokenAsync(browser, detailUrl);

        using var response = await ReorderAsync(browser, "cash", cashId, DateText, token,
            $"ledger:{secondId}", $"ledger:{firstId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertDatesAsync(firstId, secondId, Day.AddTicks(20), Day.AddTicks(10));

        using var excel = new XLWorkbook(new MemoryStream(await browser.GetByteArrayAsync($"{detailUrl}?export=1")));
        var sheet = excel.Worksheet(1);
        var table = sheet.RowsUsed().SkipWhile(row => row.Cell(2).GetString() != "Açıklama").Skip(1).ToArray();
        Assert.Collection(table,
            row => Assert.Equal(("İkinci işlem", -40m), (row.Cell(2).GetString(), row.Cell(8).GetValue<decimal>())),
            row => Assert.Equal(("İlk işlem", -140m), (row.Cell(2).GetString(), row.Cell(8).GetValue<decimal>())));

        var tokens = await _oauth.GetTokensAsync(userName, Password);
        await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("https://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {tokens.AccessToken}" }
            },
            _oauth.CreateBrowser(), ownsHttpClient: true));
        var result = await mcp.CallToolAsync("cash_bank_transactions", new Dictionary<string, object?>
        {
            ["accountKey"] = FinancialAccountHelper.CashKey(cashId), ["from"] = DateText, ["to"] = DateText
        });
        var content = Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
        Assert.True(result.IsError is not true, content);
        using var json = JsonDocument.Parse(content);
        Assert.Collection(json.RootElement.GetProperty("rows").EnumerateArray(),
            row => Assert.Equal(("İlk işlem", -140m), ReadMcpRow(row)),
            row => Assert.Equal(("İkinci işlem", -40m), ReadMcpRow(row)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transfer_edit_and_delete_still_find_the_other_leg_after_reorder(bool legacy)
    {
        int cashId, bankId, outgoingId, incomingId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cash = new CashBox { Name = "Transfer kasası", OpeningBalanceDate = Day };
            var bank = new BankAccount { Name = "Transfer bankası", OpeningBalanceDate = Day };
            db.AddRange(cash, bank, new LedgerTransaction { CashBox = cash, Date = Day.AddTicks(90) });
            await db.SaveChangesAsync();
            cashId = cash.Id;
            bankId = bank.Id;
            if (legacy)
            {
                var outgoing = new LedgerTransaction
                {
                    CashBoxId = cashId, Date = Day.AddTicks(100), Amount = 100m, Description = "Bankaya yatır: Eski",
                    PaymentChannel = PaymentChannel.Cash,
                    IncomeExpenseCategory = new() { Name = "Eski gider", Type = CategoryTypeHelper.Gider }
                };
                var incoming = new LedgerTransaction
                {
                    BankAccountId = bankId, Date = outgoing.Date, Amount = outgoing.Amount, Description = outgoing.Description,
                    PaymentChannel = PaymentChannel.Bank, TransferIsIncoming = true,
                    IncomeExpenseCategory = new() { Name = "Eski gelir", Type = CategoryTypeHelper.Gelir }
                };
                db.LedgerTransactions.AddRange(outgoing, incoming);
                await db.SaveChangesAsync();
                (outgoingId, incomingId) = (outgoing.Id, incoming.Id);
            }
            else
            {
                var transfer = await scope.ServiceProvider.GetRequiredService<CashBankTransferService>().CreateAsync(
                    new TransferRequest(FinancialAccountHelper.CashKey(cashId), FinancialAccountHelper.BankKey(bankId), 100m, Day, null));
                (outgoingId, incomingId) = (transfer.OutgoingId, transfer.IncomingId);
            }
        }

        using var browser = await LoginAsync(await CreateUserAsync(AppRoles.SistemYonetici));
        var token = await ReadTokenAsync(browser, $"/CashBank/Bank/{bankId}");
        using var reorder = await ReorderAsync(browser, "bank", bankId, DateText, token, $"ledger:{incomingId}");
        Assert.Equal(HttpStatusCode.NoContent, reorder.StatusCode);
        var reordered = await ReadRowsAsync(outgoingId, incomingId);
        Assert.Equal([Day.AddTicks(100), Day.AddTicks(10)], reordered.Select(row => row.Date));
        var groupId = reordered[0].TransferGroupId;
        Assert.NotNull(groupId);
        Assert.Equal(groupId, reordered[1].TransferGroupId);

        using var update = await browser.PostAsync("/CashBank/UpdateTransferTransaction", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token, ["transactionId"] = incomingId.ToString(), ["Kind"] = "bank",
                ["Id"] = bankId.ToString(), ["Date"] = DateText, ["Amount"] = "250",
                ["ToAccountKey"] = FinancialAccountHelper.CashKey(cashId), ["Description"] = "Güncel not"
            }));
        Assert.Equal(HttpStatusCode.Redirect, update.StatusCode);
        var edited = await ReadRowsAsync(outgoingId, incomingId);
        Assert.Collection(edited,
            row => Assert.Equal((Day.AddTicks(100), false), (row.Date, row.TransferIsIncoming)),
            row => Assert.Equal((Day.AddTicks(10), true), (row.Date, row.TransferIsIncoming)));
        Assert.All(edited, row =>
        {
            Assert.Equal(groupId, row.TransferGroupId);
            Assert.Equal(250m, row.Amount);
            Assert.Equal("Bankaya yatır: Güncel not", row.Description);
        });

        using var deletion = await browser.PostAsync("/CashBank/DeleteTransaction", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token, ["kind"] = "bank", ["accountId"] = bankId.ToString(),
                ["source"] = "ledger", ["id"] = incomingId.ToString()
            }));
        Assert.Equal(HttpStatusCode.Redirect, deletion.StatusCode);
        Assert.Empty(await ReadRowsAsync(outgoingId, incomingId));
    }

    private static (string? Description, decimal Balance) ReadMcpRow(JsonElement row) =>
        (row.GetProperty("description").GetString(), row.GetProperty("balance").GetDecimal());

    private async Task<(int CashId, int FirstId, int SecondId)> SeedMovementsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cash = new CashBox { Name = "Sıralama kasası", OpeningBalanceDate = Day };
        var first = new LedgerTransaction { CashBox = cash, Date = Day.AddTicks(80), Amount = 100m, Description = "İlk işlem" };
        var second = new LedgerTransaction { CashBox = cash, Date = Day.AddTicks(90), Amount = 40m, Description = "İkinci işlem" };
        db.LedgerTransactions.AddRange(first, second);
        await db.SaveChangesAsync();
        return (cash.Id, first.Id, second.Id);
    }

    private async Task AssertDatesAsync(int firstId, int secondId, DateTime firstDate, DateTime secondDate)
    {
        var rows = await ReadRowsAsync(firstId, secondId);
        Assert.Equal(new[] { firstDate, secondDate }, rows.Select(row => row.Date));
    }

    private async Task<LedgerTransaction[]> ReadRowsAsync(int firstId, int secondId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.LedgerTransactions.AsNoTracking().Where(x => x.Id == firstId || x.Id == secondId)
            .OrderBy(x => x.Id == firstId ? 0 : 1).ToArrayAsync();
    }

    private async Task<string> CreateUserAsync(string role)
    {
        var userName = $"reorder-{Guid.NewGuid():N}";
        await _oauth.CreateUserAsync(userName, Password, role);
        return userName;
    }

    private async Task<HttpClient> LoginAsync(string userName)
    {
        var browser = _oauth.CreateBrowser();
        using var login = await OAuthTestClient.LoginAsync(browser, userName, Password);
        return browser;
    }

    private static async Task<string> ReadTokenAsync(HttpClient browser, string path)
    {
        var html = await browser.GetStringAsync(path);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, path);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static Task<HttpResponseMessage> ReorderAsync(
        HttpClient browser, string kind, int id, string day, string? token, params string[] rows)
    {
        List<KeyValuePair<string, string>> fields = [new("kind", kind), new("id", id.ToString()), new("day", day)];
        if (token is not null)
        {
            fields.Add(new("__RequestVerificationToken", token));
        }

        fields.AddRange(rows.Select(row => new KeyValuePair<string, string>("rows", row)));
        return browser.PostAsync("/CashBank/ReorderDay", new FormUrlEncodedContent(fields));
    }
}
