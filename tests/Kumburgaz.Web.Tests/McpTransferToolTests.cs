using System.Security.Claims;
using System.Text.Json;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Mcp;
using Kumburgaz.Web.Mcp.Tools;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class McpTransferToolTests
{
    private const string Key = "key-00000001";
    private const string OtherKey = "key-00000002";
    private const string PreviewRequired = "Önce confirm: false ile önizleme alın; girdiler değiştiyse yeniden önizleyin.";
    private const string BadAccount = "Geçerli bir kasa/banka hesabı seçiniz.";

    private static readonly IDataProtectionProvider DataProtection = new EphemeralDataProtectionProvider();

    [Fact]
    public async Task Preview_writes_nothing_and_returns_token()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);

        var preview = await PreviewAsync(Tools(db), seed, "\"1.500,00\"");

        Assert.False(preview.WillWrite);
        Assert.Equal(new TransferAccount($"cash:{seed.CashId}", "Ana Kasa"), preview.From);
        Assert.Equal(new TransferAccount($"bank:{seed.BankId}", "İş Bankası - Merkez"), preview.To);
        Assert.Equal(1_500m, preview.Amount);
        Assert.Equal("2026-03-05", preview.Date);
        Assert.Equal("Bankaya yatır: ATM", preview.Description);
        Assert.False(string.IsNullOrEmpty(preview.PreviewToken));
        foreach (var part in new[] { "₺1.500,00", "05.03.2026", "Ana Kasa", "İş Bankası - Merkez" })
        {
            Assert.Contains(part, preview.Summary);
        }

        Assert.Equal(0, await db.LedgerTransactions.CountAsync());
        Assert.Equal(0, await db.McpIdempotencyKeys.CountAsync());
    }

    [Fact]
    public async Task Confirm_creates_two_legs_once_per_key_and_once_per_preview()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var token = (await PreviewAsync(tools, seed)).PreviewToken;

        var first = await ConfirmAsync(tools, seed, token, Key);
        var sameKey = await ConfirmAsync(tools, seed, token, Key);
        var otherKey = await ConfirmAsync(tools, seed, token, OtherKey);

        Assert.True(first.Created);
        Assert.Equal(first, sameKey);
        Assert.Equal(first, otherKey);
        Assert.Equal($"https://localhost/CashBank/CashBox/{seed.CashId}", first.Links.From);
        Assert.Equal($"https://localhost/CashBank/Bank/{seed.BankId}", first.Links.To);

        var legs = await db.LedgerTransactions.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, legs.Count);
        var outgoing = legs.Single(x => x.Id == first.OutgoingId);
        var incoming = legs.Single(x => x.Id == first.IncomingId);
        Assert.Equal(outgoing.TransferGroupId, incoming.TransferGroupId);
        Assert.NotNull(outgoing.TransferGroupId);
        Assert.All(legs, x => Assert.True(x.IsTransfer));
        Assert.All(legs, x => Assert.Equal(100m, x.Amount));
        Assert.False(outgoing.TransferIsIncoming);
        Assert.Equal(seed.CashId, outgoing.CashBoxId);
        Assert.True(incoming.TransferIsIncoming);
        Assert.Equal(seed.BankId, incoming.BankAccountId);
        Assert.Equal(Utc(2026, 3, 5), outgoing.Date.Date);
    }

    [Fact]
    public async Task Atm_deposit_from_cash_to_bank_uses_bankaya_yatir_prefix()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);

        var deposit = await PreviewAsync(tools, seed);
        await ConfirmAsync(tools, seed, deposit.PreviewToken, Key);
        var reverse = Assert.IsType<TransferWritePreview>(await tools.RecordTransferAsync(
            Admin(), $"bank:{seed.BankId}", $"cash:{seed.CashId}", Json("50"), "2026-03-05", "Para çekildi"));
        var plain = Assert.IsType<TransferWritePreview>(await tools.RecordTransferAsync(
            Admin(), $"cash:{seed.CashId}", $"bank:{seed.BankId}", Json("50"), "2026-03-05"));

        Assert.Equal("Bankaya yatır: ATM", deposit.Description);
        Assert.Equal("Para transferi: Para çekildi", reverse.Description);
        Assert.Equal("Bankaya yatır", plain.Description);
        Assert.All(await db.LedgerTransactions.AsNoTracking().ToListAsync(), x => Assert.Equal("Bankaya yatır: ATM", x.Description));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Requires_kasabanka_write(bool confirm)
    {
        await using var db = CreateDb();
        db.RolePermissions.AddRange(
            new RolePermission { RoleName = AppRoles.SadeceGoruntuleme, Module = AppModules.KasaBanka, CanView = true, CanWrite = false },
            new RolePermission { RoleName = AppRoles.SadeceGoruntuleme, Module = AppModules.Muhasebe, CanView = true, CanWrite = true },
            new RolePermission { RoleName = AppRoles.SadeceGoruntuleme, Module = AppModules.Tahsilatlar, CanView = true, CanWrite = true });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<McpException>(() => Tools(db).RecordTransferAsync(
            User(AppRoles.SadeceGoruntuleme), "bozuk", "bozuk", Json("\"bozuk\""), confirm: confirm));

        Assert.Contains("yetkiniz yok", ex.Message);
        Assert.Contains(AppModules.KasaBanka, ex.Message);
        Assert.Equal(0, await db.LedgerTransactions.CountAsync());
    }

    [Fact]
    public async Task Rejects_same_account_and_inactive_account()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);

        await RejectedAsync("Kaynak ve hedef hesap aynı olamaz.", () => tools.RecordTransferAsync(
            Admin(), $"cash:{seed.CashId}", $"CASH:{seed.CashId}", Json("10")));
        await RejectedAsync(BadAccount, () => tools.RecordTransferAsync(
            Admin(), $"cash:{seed.CashId}", $"bank:{seed.InactiveBankId}", Json("10")));
        await RejectedAsync(BadAccount, () => tools.RecordTransferAsync(
            Admin(), $"cash:{seed.InactiveCashId}", $"bank:{seed.BankId}", Json("10")));
        await RejectedAsync(BadAccount, () => tools.RecordTransferAsync(
            Admin(), $"cash:{seed.CashId}", "bank:99999", Json("10")));
        await RejectedAsync(BadAccount, () => tools.RecordTransferAsync(
            Admin(), $"cash:{seed.CashId}", "hesap", Json("10")));
        await RejectedAsync("Geçerli bir tutar giriniz.", () => tools.RecordTransferAsync(
            Admin(), $"cash:{seed.CashId}", $"bank:{seed.BankId}", Json("0")));
        var tooPrecise = await Assert.ThrowsAsync<McpException>(() => tools.RecordTransferAsync(
            Admin(), $"cash:{seed.CashId}", $"bank:{seed.BankId}", Json("10.001")));
        Assert.Contains("2 ondalık", tooPrecise.Message);
        await RejectedAsync("description en fazla 230 karakter olabilir.", () => tools.RecordTransferAsync(
            Admin(), $"cash:{seed.CashId}", $"bank:{seed.BankId}", Json("10"), description: new string('a', 231)));

        // Önizlemeden sonra hesap pasife alınırsa onay yazmaz ve anahtarı harcamaz.
        var token = (await PreviewAsync(tools, seed)).PreviewToken;
        var bank = await db.BankAccounts.SingleAsync(x => x.Id == seed.BankId);
        bank.Active = false;
        await db.SaveChangesAsync();
        await RejectedAsync(BadAccount, () => ConfirmAsync(tools, seed, token, Key));
        Assert.Equal(0, await db.LedgerTransactions.CountAsync());
        Assert.Equal(0, await db.McpIdempotencyKeys.CountAsync());
    }

    [Fact]
    public async Task Confirm_needs_a_matching_preview_and_a_valid_key()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var token = (await PreviewAsync(tools, seed)).PreviewToken;

        await RejectedAsync(PreviewRequired, () => ConfirmAsync(tools, seed, null, Key));
        await RejectedAsync(PreviewRequired, () => ConfirmAsync(tools, seed, token, Key, amount: "101"));
        await RejectedAsync("confirm: true için idempotency_key gönderin.", () => ConfirmAsync(tools, seed, token, null));
        await RejectedAsync("idempotency_key 8-100 karakter olmalı.", () => ConfirmAsync(tools, seed, token, "kisa"));
        Assert.Equal(0, await db.LedgerTransactions.CountAsync());
    }

    private static async Task<TransferWritePreview> PreviewAsync(WriteTools tools, Seed seed, string amount = "100")
        => Assert.IsType<TransferWritePreview>(await tools.RecordTransferAsync(
            Admin(), $"cash:{seed.CashId}", $"bank:{seed.BankId}", Json(amount), "2026-03-05", "ATM"));

    private static async Task<TransferWriteReceipt> ConfirmAsync(
        WriteTools tools, Seed seed, string? token, string? key, string amount = "100")
        => Assert.IsType<TransferWriteReceipt>(await tools.RecordTransferAsync(
            Admin(), $"cash:{seed.CashId}", $"bank:{seed.BankId}", Json(amount), "2026-03-05", "ATM",
            confirm: true, idempotency_key: key, preview_token: token));

    private static async Task RejectedAsync(string message, Func<Task> call)
        => Assert.Equal(message, (await Assert.ThrowsAsync<McpException>(call)).Message);

    private sealed record Seed(int CashId, int InactiveCashId, int BankId, int InactiveBankId);

    private static async Task<Seed> SeedAsync(ApplicationDbContext db)
    {
        var cash = new CashBox { Name = "Ana Kasa", OpeningBalanceDate = Utc(2025, 1, 1) };
        var inactiveCash = new CashBox { Name = "Eski Kasa", OpeningBalanceDate = Utc(2025, 1, 1), Active = false };
        var bank = new BankAccount { Name = "İş Bankası", Branch = "Merkez", OpeningBalanceDate = Utc(2025, 1, 1) };
        var inactiveBank = new BankAccount { Name = "Eski Banka", OpeningBalanceDate = Utc(2025, 1, 1), Active = false };
        db.AddRange(cash, inactiveCash, bank, inactiveBank);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new Seed(cash.Id, inactiveCash.Id, bank.Id, inactiveBank.Id);
    }

    private static WriteTools Tools(ApplicationDbContext db)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OAuth:Issuer"] = "https://localhost/" })
            .Build();
        return new WriteTools(
            db,
            new PermissionService(db, new MemoryCache(new MemoryCacheOptions())),
            new CollectionService(db),
            new LedgerEntryService(db),
            new DuesLedgerRowService(db),
            new UnitLedgerService(db, new DuesLedgerRowService(db)),
            new McpIdempotencyStore(db),
            new McpPreviewTokens(DataProtection),
            new McpLinks(config),
            new CashBankTransferService(db, new TransactionOrderService(db)));
    }

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ClaimsPrincipal Admin() => User(AppRoles.SistemYonetici);

    private static ClaimsPrincipal User(string role)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1"), new Claim(ClaimTypes.Role, role)], "Test"));

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);
}
