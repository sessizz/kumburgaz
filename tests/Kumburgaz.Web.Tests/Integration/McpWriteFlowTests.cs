using System.Globalization;
using System.Text.Json;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Mcp;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Kumburgaz.Web.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class McpWriteFlowTests(KumburgazAppFactory factory)
{
    private const string Password = "test12345";

    private readonly OAuthTestClient _oauth = new(factory);

    // MCP'de yazan yalnız üç araç var; hepsi yıkıcı olmayan ekleme.
    [Fact]
    public async Task Only_three_tools_can_write()
    {
        await using var client = await ConnectAsync(AppRoles.SistemYonetici);

        var tools = (await client.ListToolsAsync()).ToDictionary(x => x.Name);
        var writers = tools.Values.Where(x => x.ProtocolTool.Annotations?.ReadOnlyHint is not true).ToList();

        Assert.Equal(["create_expense", "create_income", "record_collection"], writers.Select(x => x.Name).Order());
        Assert.All(writers, x =>
        {
            Assert.False(x.ProtocolTool.Annotations!.DestructiveHint);
            Assert.False(x.ProtocolTool.Annotations.IdempotentHint);
        });
        Assert.Equal(
            ["unitId", "amount", "accountKey", "date", "period", "isReceipt", "referenceNo", "note", "confirm", "idempotency_key", "preview_token"],
            Parameters(tools["record_collection"]));
        foreach (var name in new[] { "create_expense", "create_income" })
        {
            Assert.Equal(
                ["categoryId", "amount", "accountKey", "description", "date", "confirm", "idempotency_key", "preview_token"],
                Parameters(tools[name]));
        }

        Assert.Equal(["unitId", "amount", "accountKey"], Required(tools["record_collection"]));
        Assert.Contains("record_collection", tools["create_income"].Description);
        Assert.Contains("preview_token", client.ServerInstructions);
        Assert.Contains("previewToken", client.ServerInstructions);
        Assert.Contains("idempotency_key", client.ServerInstructions);
    }

    [Fact]
    public async Task Collection_created_via_mcp_is_audited_as_claude()
    {
        var seed = await SeedAsync();
        await using var client = await ConnectAsync(AppRoles.MuhasebeGorevli);
        var token = PreviewToken(await CallAsync(client, "record_collection", Collection(seed)));

        var created = await CallAsync(client, "record_collection", Confirmed(Collection(seed), token, NewKey()));

        var id = created.GetProperty("id").GetInt32().ToString(CultureInfo.InvariantCulture);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var log = Assert.Single(await db.AuditLogs.AsNoTracking()
            .Where(x => x.EntityName == nameof(Collection) && x.EntityId == id && x.Action == AuditAction.Create)
            .ToListAsync());
        Assert.EndsWith("(Claude)", log.UserName);
        Assert.False(string.IsNullOrEmpty(log.UserId));
    }

    [Fact]
    public async Task Collection_preview_confirm_and_retry_end_to_end()
    {
        var seed = await SeedAsync();
        await using var client = await ConnectAsync(AppRoles.MuhasebeGorevli);
        var key = NewKey();

        var preview = await CallAsync(client, "record_collection", Collection(seed, "2.000,00"));
        Assert.False(preview.GetProperty("willWrite").GetBoolean());
        Assert.Contains("₺2.000,00", preview.GetProperty("summary").GetString());
        Assert.Equal(2_000m, preview.GetProperty("amount").GetDecimal());
        Assert.Equal(seed.CashName, preview.GetProperty("account").GetString());
        Assert.Contains(seed.UnitNo, preview.GetProperty("unit").GetString());
        Assert.Equal("2026-03-05", preview.GetProperty("date").GetString());
        Assert.Equal(500m, preview.GetProperty("advance").GetDecimal());
        Assert.False(preview.TryGetProperty("category", out _));
        Assert.Equal(1_500m, preview.GetProperty("lines").EnumerateArray().Sum(x => x.GetProperty("applied").GetDecimal()));
        Assert.Equal(0, await CountCollectionsAsync(seed));

        var created = await CallAsync(client, "record_collection", Confirmed(Collection(seed, 2000), PreviewToken(preview), key));
        var retry = await CallAsync(client, "record_collection", Confirmed(Collection(seed, "2000,00"), PreviewToken(preview), key));

        Assert.True(created.GetProperty("created").GetBoolean());
        var id = created.GetProperty("id").GetInt32();
        Assert.Equal($"https://localhost/Collections/Edit/{id}", created.GetProperty("link").GetString());
        Assert.Equal(Lines(preview), Lines(created));
        Assert.Equal(500m, created.GetProperty("advance").GetDecimal());
        Assert.Equal(created.GetRawText(), retry.GetRawText());
        Assert.Equal(1, await CountCollectionsAsync(seed));
        Assert.Equal(1_500m, (await AllocationsAsync(seed)).Sum(x => x.AppliedAmount));
    }

    [Fact]
    public async Task Expense_and_income_preview_confirm_and_retry_end_to_end()
    {
        var seed = await SeedAsync();
        await using var client = await ConnectAsync(AppRoles.MuhasebeGorevli);

        foreach (var (tool, categoryId) in new[] { ("create_expense", seed.ExpenseCategoryId), ("create_income", seed.IncomeCategoryId) })
        {
            var key = NewKey();
            var input = new Dictionary<string, object?>
            {
                ["categoryId"] = categoryId, ["amount"] = 150.25, ["accountKey"] = $"cash:{seed.CashId}",
                ["description"] = "Şubat faturası", ["date"] = "2026-03-05"
            };

            var preview = await CallAsync(client, tool, input);
            Assert.False(preview.GetProperty("willWrite").GetBoolean());
            Assert.False(preview.TryGetProperty("lines", out _));
            var created = await CallAsync(client, tool, Confirmed(input, PreviewToken(preview), key));
            var retry = await CallAsync(client, tool, Confirmed(input, PreviewToken(preview), key));

            Assert.Equal(created.GetRawText(), retry.GetRawText());
            Assert.Equal($"https://localhost/Ledger/Edit/{created.GetProperty("id").GetInt32()}", created.GetProperty("link").GetString());
            await using var scope = factory.Services.CreateAsyncScope();
            var saved = Assert.Single(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().LedgerTransactions
                .AsNoTracking().Where(x => x.IncomeExpenseCategoryId == categoryId).ToListAsync());
            Assert.Equal(150.25m, saved.Amount);
            Assert.Equal("Şubat faturası", saved.Description);
        }
    }

    // Aynı anahtarla üst üste binen çağrılar (zaman aşımından sonra tekrar): tek tahsilat, yarım dağıtım yok.
    [Fact]
    public async Task Simultaneous_confirm_calls_with_one_key_create_exactly_one_collection()
    {
        var seed = await SeedAsync();
        await using var client = await ConnectAsync(AppRoles.MuhasebeGorevli);
        var key = NewKey();
        var token = PreviewToken(await CallAsync(client, "record_collection", Collection(seed, 900)));

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(
            () => CallAsync(client, "record_collection", Confirmed(Collection(seed, 900), token, key)))));

        Assert.Single(results.Select(x => x.GetRawText()).Distinct());
        var id = results[0].GetProperty("id").GetInt32();
        Assert.Equal(1, await CountCollectionsAsync(seed));
        var allocations = await AllocationsAsync(seed);
        Assert.All(allocations, x => Assert.Equal(id, x.CollectionId));
        Assert.Equal(900m, allocations.Sum(x => x.AppliedAmount));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var remaining = await db.DuesInstallments.AsNoTracking().Where(x => x.UnitId == seed.UnitId)
            .OrderBy(x => x.Period).Select(x => x.RemainingAmount).ToListAsync();
        Assert.Equal([0m, 600m], remaining);
        Assert.Single(await db.McpIdempotencyKeys.AsNoTracking().Where(x => x.Key == key).ToListAsync());
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.EntityName == nameof(Collection)
            && x.EntityId == id.ToString(CultureInfo.InvariantCulture) && x.Action == AuditAction.Create));
    }

    // Claude araçları paralel çağırabilir: farklı anahtarlı eşzamanlı tahsilatlar aynı borcu iki kez kapatmaz.
    [Fact]
    public async Task Parallel_collections_with_different_keys_do_not_over_allocate_the_debt()
    {
        var seed = await SeedAsync();
        await using var client = await ConnectAsync(AppRoles.MuhasebeGorevli);
        var token = PreviewToken(await CallAsync(client, "record_collection", Collection(seed, 900)));

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(
            () => CallAsync(client, "record_collection", Confirmed(Collection(seed, 900), token, NewKey())))));

        Assert.Equal(4, results.Select(x => x.GetProperty("id").GetInt32()).Distinct().Count());
        Assert.Equal(4, await CountCollectionsAsync(seed));
        Assert.Equal(1_500m, (await AllocationsAsync(seed)).Sum(x => x.AppliedAmount));
        Assert.Equal(1_500m, results.SelectMany(Lines).Sum(x => x.Applied));
        Assert.Equal(4 * 900m - 1_500m, results.Sum(x => x.GetProperty("advance").GetDecimal()));
    }

    [Fact]
    public async Task Confirm_needs_a_fresh_preview_of_the_same_user_and_inputs()
    {
        var seed = await SeedAsync();
        await using var client = await ConnectAsync(AppRoles.MuhasebeGorevli);
        await using var other = await ConnectAsync(AppRoles.MuhasebeGorevli);
        var input = Collection(seed);
        var token = PreviewToken(await CallAsync(client, "record_collection", input));
        var foreign = PreviewToken(await CallAsync(other, "record_collection", input));
        var protector = factory.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(McpPreviewTokens.Purpose).ToTimeLimitedDataProtector();
        var expired = protector.Protect(protector.Unprotect(token), TimeSpan.FromSeconds(-1));

        foreach (var rejected in new[]
                 {
                     Confirmed(input, null, NewKey()),
                     Confirmed(input, expired, NewKey()),
                     Confirmed(input, foreign, NewKey()),
                     Confirmed(Collection(seed, 101), token, NewKey())
                 })
        {
            var result = await client.CallToolAsync("record_collection", rejected);
            Assert.True(result.IsError);
            Assert.Contains("Önce confirm: false ile önizleme alın; girdiler değiştiyse yeniden önizleyin.", Text(result));
        }

        var withoutKey = await client.CallToolAsync("record_collection", Confirmed(input, token, null));
        Assert.True(withoutKey.IsError);
        Assert.Contains("confirm: true için idempotency_key gönderin.", Text(withoutKey));
        Assert.Equal(0, await CountCollectionsAsync(seed));
    }

    // Salt görüntüleme rolünün token'ı hiçbir yazma aracını çalıştıramaz.
    [Fact]
    public async Task Read_only_role_cannot_use_write_tools()
    {
        var seed = await SeedAsync();
        await using var client = await ConnectAsync(AppRoles.SadeceGoruntuleme);

        var result = await client.CallToolAsync("record_collection", Collection(seed));

        Assert.True(result.IsError);
        Assert.Contains("yetkiniz yok", Text(result));
    }

    private sealed record Seed(int UnitId, string UnitNo, int CashId, string CashName, int ExpenseCategoryId, int IncomeCategoryId);

    // Devir borcu 600; açık taksitler 2025-2026 (300) ve 2026-2027 (600): toplam borç 1500.
    private async Task<Seed> SeedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = Guid.NewGuid().ToString("N")[..10];
        var unit = new Unit { BlockId = 1, UnitNo = id, OwnerName = "Malik", OpeningBalance = -600m, OpeningBalanceDate = Utc(2025, 5, 26) };
        var duesType = new DuesType { Name = $"Tip {id}", Amount = 600m, Active = true };
        var group = new BillingGroup { Name = $"Grup {id}", DuesType = duesType, EffectiveStartPeriod = "2025-2026", Active = true };
        var cash = new CashBox { Name = $"Kasa {id}", OpeningBalanceDate = Utc(2025, 1, 1) };
        var expense = new IncomeExpenseCategory { Name = $"Gider {id}", Type = CategoryTypeHelper.Gider };
        var income = new IncomeExpenseCategory { Name = $"Gelir {id}", Type = CategoryTypeHelper.Gelir };
        db.AddRange(unit, duesType, group, cash, expense, income);
        db.Add(new BillingGroupUnit { BillingGroup = group, Unit = unit, StartPeriod = "2025-2026" });
        foreach (var (period, year, amount) in new[] { ("2025-2026", 2025, 300m), ("2026-2027", 2026, 600m) })
        {
            db.Add(new DuesInstallment
            {
                Unit = unit, BillingGroup = group, Period = period, AccrualDate = Utc(year, 8, 1), DueDate = Utc(year, 8, 1),
                Amount = amount, RemainingAmount = amount, Status = InstallmentStatus.Open
            });
        }

        await db.SaveChangesAsync();
        return new Seed(unit.Id, id, cash.Id, cash.Name, expense.Id, income.Id);
    }

    private static Dictionary<string, object?> Collection(Seed seed, object? amount = null) => new()
    {
        ["unitId"] = seed.UnitId, ["amount"] = amount ?? 100, ["accountKey"] = $"cash:{seed.CashId}",
        ["date"] = "2026-03-05", ["period"] = "devir"
    };

    private static Dictionary<string, object?> Confirmed(Dictionary<string, object?> input, string? previewToken, string? key)
    {
        var confirmed = new Dictionary<string, object?>(input) { ["confirm"] = true };
        if (previewToken is not null)
        {
            confirmed["preview_token"] = previewToken;
        }

        if (key is not null)
        {
            confirmed["idempotency_key"] = key;
        }

        return confirmed;
    }

    private static string NewKey() => Guid.NewGuid().ToString();

    private static string PreviewToken(JsonElement preview) => preview.GetProperty("previewToken").GetString()!;

    private static List<(string Label, decimal Applied, decimal RemainingAfter)> Lines(JsonElement result)
        => result.GetProperty("lines").EnumerateArray()
            .Select(x => (x.GetProperty("label").GetString()!, x.GetProperty("applied").GetDecimal(), x.GetProperty("remainingAfter").GetDecimal()))
            .ToList();

    private static async Task<JsonElement> CallAsync(McpClient client, string tool, Dictionary<string, object?> input)
    {
        var result = await client.CallToolAsync(tool, input);
        Assert.True(result.IsError is not true, Text(result));
        return JsonDocument.Parse(Text(result)).RootElement;
    }

    private async Task<int> CountCollectionsAsync(Seed seed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Collections.CountAsync(x => x.UnitId == seed.UnitId);
    }

    // Dairenin devir ve taksit tahsisatlarının tamamı (silinmemiş).
    private async Task<List<CollectionAllocation>> AllocationsAsync(Seed seed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().CollectionAllocations.AsNoTracking()
            .Where(x => x.UnitId == seed.UnitId || x.DuesInstallment!.UnitId == seed.UnitId)
            .ToListAsync();
    }

    private static string[] Parameters(McpClientTool tool)
        => tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(x => x.Name).ToArray();

    private static string[] Required(McpClientTool tool)
        => tool.ProtocolTool.InputSchema.GetProperty("required").EnumerateArray().Select(x => x.GetString()!).ToArray();

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
