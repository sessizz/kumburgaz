using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Mcp;
using Kumburgaz.Web.Mcp.Tools;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class McpWriteToolsTests
{
    private const string Key = "key-00000001";
    private const string OtherKey = "key-00000002";
    private const string PreviewRequired = "Önce confirm: false ile önizleme alın; girdiler değiştiyse yeniden önizleyin.";
    private const string KeyReused = "Bu idempotency_key farklı bir işlem için kullanılmış; yeni işlem için yeni bir anahtar gönderin.";
    private const string BadAccount = "Geçerli bir kasa/banka hesabı seçiniz.";

    private static readonly IDataProtectionProvider DataProtection = new EphemeralDataProtectionProvider();

    [Fact]
    public async Task Record_collection_preview_writes_nothing()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var expected = await PlanAsync(db, seed, 1_000m, "devir");

        var preview = await PreviewAsync(Tools(db), seed, amount: "1000", period: "devir");

        Assert.False(preview.WillWrite);
        Assert.Equal("A-5", preview.Unit);
        Assert.Equal("Ana Kasa", preview.Account);
        Assert.Equal(1_000m, preview.Amount);
        Assert.Equal("2026-03-05", preview.Date);
        Assert.Null(preview.Category);
        Assert.Equal(expected.Lines.Select(x => new AllocatedLine(x.Label, x.Applied, x.RemainingAfter)), preview.Lines);
        Assert.Equal(
            [
                new AllocatedLine("Devir borcu", 300m, 0m),
                new AllocatedLine("2025-2026 Aidat Tipi", 500m, 0m),
                new AllocatedLine("2026-2027 Aidat Tipi", 200m, 300m)
            ],
            preview.Lines);
        Assert.Equal(0m, preview.Advance);
        foreach (var part in new[] { "A-5", "₺1.000,00", "05.03.2026", "Ana Kasa", "Devir borcu", "₺300,00" })
        {
            Assert.Contains(part, preview.Summary);
        }

        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(0, await db.Collections.CountAsync());
        Assert.Equal(0, await db.CollectionAllocations.CountAsync());
        Assert.Equal(0, await db.McpIdempotencyKeys.CountAsync());
        Assert.Equal(1_000m, await db.DuesInstallments.SumAsync(x => x.RemainingAmount));
    }

    // Ağ yeniden denemesi: aynı anahtar tek kayıt, farklı anahtar ayrı kayıt.
    // İlk kayıt dönemi kapattığı için tekrar, dönem kontrolüne takılmadan saklı sonucu dönmeli.
    [Fact]
    public async Task Record_collection_confirm_creates_once_per_idempotency_key()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var token = (await PreviewAsync(tools, seed, amount: "500", period: "2025-2026")).PreviewToken;

        var first = await ConfirmAsync(tools, seed, token, Key, amount: "500", period: "2025-2026");
        var retry = await ConfirmAsync(tools, seed, token, Key, amount: "500", period: "2025-2026");

        Assert.True(first.Created);
        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(first.Link, retry.Link);
        Assert.Equal(first.Lines, retry.Lines);
        Assert.Equal(first.Advance, retry.Advance);
        Assert.Equal($"https://localhost/Collections/Edit/{first.Id}", first.Link);
        Assert.Equal([new AllocatedLine("2025-2026 Aidat Tipi", 500m, 0m)], first.Lines);
        Assert.Equal(0m, first.Advance);
        var saved = Assert.Single(await db.Collections.AsNoTracking().ToListAsync());
        Assert.Equal(seed.UnitId, saved.UnitId);
        Assert.Equal(500m, saved.Amount);
        Assert.Equal(Utc(2026, 3, 5), saved.Date);
        Assert.Equal(seed.CashId, saved.CashBoxId);
        Assert.Equal(PaymentChannel.Cash, saved.PaymentChannel);

        var generalToken = (await PreviewAsync(tools, seed, amount: "100")).PreviewToken;
        var second = await ConfirmAsync(tools, seed, generalToken, OtherKey, amount: "100");
        var third = await ConfirmAsync(tools, seed, generalToken, "key-00000003", amount: "100");
        Assert.Equal(3, new HashSet<int> { first.Id, second.Id, third.Id }.Count);
        Assert.Equal(3, await db.Collections.CountAsync());
    }

    // Devir alacağı 5.000, taksit 12.000, ödeme 7.000: taksitin ham kalanı 5.000 görünse de dairenin borcu biter.
    [Fact]
    public async Task Record_collection_balance_after_counts_carried_credit()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var unit = await db.Units.SingleAsync(x => x.Id == seed.OtherUnitId);
        unit.OpeningBalance = 5_000m;
        unit.OpeningBalanceDate = Utc(2025, 5, 26);
        db.DuesInstallments.Add(new DuesInstallment
        {
            UnitId = unit.Id, BillingGroupId = (await db.BillingGroups.SingleAsync()).Id, Period = "2026-2027",
            AccrualDate = Utc(2026, 8, 1), DueDate = Utc(2026, 8, 1), Amount = 12_000m, RemainingAmount = 12_000m,
            Status = InstallmentStatus.Open
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var tools = Tools(db);

        var preview = (WritePreview)await tools.RecordCollectionAsync(
            Admin(), unit.Id, Json("7000"), $"cash:{seed.CashId}", "2026-08-05", "2026-2027");
        var receipt = (WriteReceipt)await tools.RecordCollectionAsync(
            Admin(), unit.Id, Json("7000"), $"cash:{seed.CashId}", "2026-08-05", "2026-2027",
            confirm: true, idempotency_key: Key, preview_token: preview.PreviewToken);
        var retry = (WriteReceipt)await tools.RecordCollectionAsync(
            Admin(), unit.Id, Json("7000"), $"cash:{seed.CashId}", "2026-08-05", "2026-2027",
            confirm: true, idempotency_key: Key);

        Assert.Equal([new AllocatedLine("2026-2027 Aidat Tipi", 7_000m, 5_000m)], preview.Lines);
        Assert.Equal(0m, preview.BalanceAfter);
        Assert.Contains("kalan borç: ₺0,00", preview.Summary);
        Assert.DoesNotContain("₺5.000,00", preview.Summary);
        Assert.Equal(preview.Lines, receipt.Lines);
        Assert.Equal(0m, receipt.BalanceAfter);
        Assert.Equal((await LedgerOf(db).BuildAsync(unit.Id))!.Summary.NetBalance, receipt.BalanceAfter);
        Assert.Equal(0m, retry.BalanceAfter);
    }

    // balanceAfter: pozitif borç, negatif alacak (avans); ekrandaki daire bakiyesiyle aynı hesap.
    [Theory]
    [InlineData("1000", "kalan borç: ₺300,00.", 300)]
    [InlineData("1300", "kalan borç: ₺0,00.", 0)]
    [InlineData("1500", "kalan borç: ₺0,00 (daire ₺200,00 alacaklı).", -200)]
    public async Task Record_collection_reports_unit_balance_after_the_payment(string amount, string summary, int balanceAfter)
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);

        var preview = await PreviewAsync(tools, seed, amount, "devir");
        var receipt = await ConfirmAsync(tools, seed, preview.PreviewToken, Key, amount, "devir");

        Assert.Equal(balanceAfter, preview.BalanceAfter);
        Assert.Contains(summary, preview.Summary);
        Assert.Equal(balanceAfter, receipt.BalanceAfter);
        Assert.Equal((await LedgerOf(db).BuildAsync(seed.UnitId))!.Summary.NetBalance, receipt.BalanceAfter);
    }

    // Önizlemeden sonra başka tahsilat girilirse kayıt güncel planla yazılır, sonuç gerçekten yazılanı gösterir.
    [Fact]
    public async Task Record_collection_confirm_uses_current_state_not_stale_preview()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var preview = await PreviewAsync(tools, seed, amount: "1000", period: "devir");

        var service = new CollectionService(db);
        await service.CreateAsync(await service.BuildForUnitAsync(
            seed.UnitId, 400m, Utc(2026, 3, 1), $"cash:{seed.CashId}", "devir", false, null, null));
        var current = await PlanAsync(db, seed, 1_000m, "devir");
        var receipt = await ConfirmAsync(tools, seed, preview.PreviewToken, Key, amount: "1000", period: "devir");

        Assert.NotEqual(preview.Lines, receipt.Lines);
        Assert.Equal(current.Lines.Select(x => new AllocatedLine(x.Label, x.Applied, x.RemainingAfter)), receipt.Lines);
        Assert.Equal(
            [new AllocatedLine("2025-2026 Aidat Tipi", 400m, 0m), new AllocatedLine("2026-2027 Aidat Tipi", 500m, 0m)],
            receipt.Lines);
        Assert.Equal(100m, receipt.Advance);
        Assert.Equal(current.Advance, receipt.Advance);
    }

    // Kısmen kapanan devir: kalan, devir borcunun gerçek kalanıdır.
    [Fact]
    public async Task Record_collection_receipt_reports_remaining_devir()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var token = (await PreviewAsync(tools, seed, amount: "120", period: "devir")).PreviewToken;

        var receipt = await ConfirmAsync(tools, seed, token, Key, amount: "120", period: "devir");

        Assert.Equal([new AllocatedLine("Devir borcu", 120m, 180m)], receipt.Lines);
    }

    [Fact]
    public async Task Confirm_without_idempotency_key_is_rejected()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var collectionToken = (await PreviewAsync(tools, seed)).PreviewToken;
        var expenseToken = (await ExpensePreviewAsync(tools, seed)).PreviewToken;

        foreach (var key in new[] { null, "", "   " })
        {
            await RejectedAsync("confirm: true için idempotency_key gönderin.", () => ConfirmAsync(tools, seed, collectionToken, key));
            await RejectedAsync("confirm: true için idempotency_key gönderin.", () => ExpenseConfirmAsync(tools, seed, expenseToken, key));
        }

        await RejectedAsync("idempotency_key 8-100 karakter olmalı.", () => ConfirmAsync(tools, seed, collectionToken, "kisa"));
        await RejectedAsync("idempotency_key 8-100 karakter olmalı.", () => ConfirmAsync(tools, seed, collectionToken, new string('k', 101)));
        await AssertNothingWrittenAsync(db);
    }

    [Fact]
    public async Task Create_expense_rejects_income_category()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);

        await RejectedAsync("Seçilen kategori bir gider kategorisi değil.",
            () => ExpensePreviewAsync(tools, seed, categoryId: seed.IncomeCategoryId));
        await RejectedAsync("Seçilen kategori bir gelir kategorisi değil.",
            () => IncomePreviewAsync(tools, seed, categoryId: seed.ExpenseCategoryId));
        await RejectedAsync("Kategori bulunamadı.", () => ExpensePreviewAsync(tools, seed, categoryId: seed.InactiveCategoryId));
        await RejectedAsync("Kategori bulunamadı.", () => ExpensePreviewAsync(tools, seed, categoryId: 99_999));
        await AssertNothingWrittenAsync(db);
    }

    // Yetki her şeyden önce: önizleme de kayıt da yazma yetkisi ister.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Write_tool_requires_write_permission(bool confirm)
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        db.RolePermissions.AddRange(new[] { AppModules.Tahsilatlar, AppModules.Muhasebe }.Select(module =>
            new RolePermission { RoleName = AppRoles.SadeceGoruntuleme, Module = module, CanView = true, CanWrite = false }));
        await db.SaveChangesAsync();
        var tools = Tools(db);
        var user = User(AppRoles.SadeceGoruntuleme);

        var denied = new Func<Task>[]
        {
            () => tools.RecordCollectionAsync(user, seed.UnitId, Json("\"bozuk\""), "bozuk", confirm: confirm),
            () => tools.CreateExpenseAsync(user, seed.ExpenseCategoryId, Json("\"bozuk\""), "bozuk", "x", confirm: confirm),
            () => tools.CreateIncomeAsync(user, seed.IncomeCategoryId, Json("\"bozuk\""), "bozuk", "x", confirm: confirm)
        };

        foreach (var call in denied)
        {
            var ex = await Assert.ThrowsAsync<McpException>(call);
            Assert.Contains("yetkiniz yok", ex.Message);
        }
    }

    // Modül yazma yetkisi yalnız kendi araçlarını açar.
    [Fact]
    public async Task Each_module_write_permission_unlocks_only_its_tools()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        db.RolePermissions.AddRange(
            new RolePermission { RoleName = AppRoles.Personel, Module = AppModules.Tahsilatlar, CanView = true, CanWrite = true },
            new RolePermission { RoleName = AppRoles.MuhasebeGorevli, Module = AppModules.Muhasebe, CanView = true, CanWrite = true });
        await db.SaveChangesAsync();
        var tools = Tools(db);
        var collector = User(AppRoles.Personel);
        var accountant = User(AppRoles.MuhasebeGorevli);

        Assert.False((await PreviewAsync(tools, seed, user: collector)).WillWrite);
        Assert.Contains("Muhasebe", (await Assert.ThrowsAsync<McpException>(() => ExpensePreviewAsync(tools, seed, user: collector))).Message);
        Assert.False((await ExpensePreviewAsync(tools, seed, user: accountant)).WillWrite);
        Assert.False((await IncomePreviewAsync(tools, seed, user: accountant)).WillWrite);
        Assert.Contains("Tahsilatlar", (await Assert.ThrowsAsync<McpException>(() => PreviewAsync(tools, seed, user: accountant))).Message);
    }

    [Fact]
    public async Task Create_expense_preview_then_confirm_writes_once()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);

        var preview = await ExpensePreviewAsync(tools, seed);
        await AssertNothingWrittenAsync(db);
        var first = await ExpenseConfirmAsync(tools, seed, preview.PreviewToken, Key);
        var retry = await ExpenseConfirmAsync(tools, seed, preview.PreviewToken, Key);

        Assert.False(preview.WillWrite);
        Assert.Equal("Elektrik", preview.Category);
        Assert.Equal("İş Bankası - Merkez", preview.Account);
        Assert.Equal(1_250.50m, preview.Amount);
        Assert.Equal("2026-03-05", preview.Date);
        Assert.Null(preview.Unit);
        Assert.Null(preview.Lines);
        Assert.Null(preview.Advance);
        Assert.Null(preview.BalanceAfter);
        Assert.Null(first.BalanceAfter);
        foreach (var part in new[] { "Elektrik", "₺1.250,50", "05.03.2026", "İş Bankası - Merkez", "Mart faturası" })
        {
            Assert.Contains(part, preview.Summary);
        }

        Assert.Equal(first, retry);
        Assert.True(first.Created);
        Assert.Equal($"https://localhost/Ledger/Edit/{first.Id}", first.Link);
        Assert.Null(first.Lines);
        Assert.Null(first.Advance);
        var saved = Assert.Single(await db.LedgerTransactions.AsNoTracking().ToListAsync());
        Assert.Equal(first.Id, saved.Id);
        Assert.Equal(seed.ExpenseCategoryId, saved.IncomeExpenseCategoryId);
        Assert.Equal(1_250.50m, saved.Amount);
        Assert.Equal(Utc(2026, 3, 5), saved.Date);
        Assert.Equal(seed.BankId, saved.BankAccountId);
        Assert.Equal("Mart faturası", saved.Description);
        Assert.False(saved.IsTransfer);
    }

    [Fact]
    public async Task Create_income_preview_then_confirm_writes_once()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);

        var preview = await IncomePreviewAsync(tools, seed);
        var first = await IncomeConfirmAsync(tools, seed, preview.PreviewToken, Key);
        var retry = await IncomeConfirmAsync(tools, seed, preview.PreviewToken, Key);

        Assert.Equal("Kira", preview.Category);
        Assert.Equal(first, retry);
        var saved = Assert.Single(await db.LedgerTransactions.AsNoTracking().ToListAsync());
        Assert.Equal(first.Id, saved.Id);
        Assert.Equal(seed.IncomeCategoryId, saved.IncomeExpenseCategoryId);
    }

    [Fact]
    public async Task Confirm_without_valid_preview_token_is_rejected()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var valid = (await PreviewAsync(tools, seed)).PreviewToken;

        foreach (var token in new[] { null, "", "gecersiz", valid[..^4], valid + "AAAA" })
        {
            await RejectedAsync(PreviewRequired, () => ConfirmAsync(tools, seed, token, Key));
        }

        // Aynı girdilerin bile önizlemesi alınmadan gider yazılamaz.
        await RejectedAsync(PreviewRequired, () => ExpenseConfirmAsync(tools, seed, null, Key));
        await AssertNothingWrittenAsync(db);
    }

    [Fact]
    public async Task Preview_token_expires_after_30_minutes()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var protector = DataProtection.CreateProtector(McpPreviewTokens.Purpose).ToTimeLimitedDataProtector();

        var token = (await PreviewAsync(tools, seed)).PreviewToken;
        var payload = protector.Unprotect(token, out var expiration);
        var expired = protector.Protect(payload, TimeSpan.FromSeconds(-1));

        Assert.InRange(expiration - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(30));
        await RejectedAsync(PreviewRequired, () => ConfirmAsync(tools, seed, expired, Key));
        await AssertNothingWrittenAsync(db);
        // Aynı içerik süresi dolmadan geçerlidir: ret yalnız süreden.
        Assert.True((await ConfirmAsync(tools, seed, protector.Protect(payload, TimeSpan.FromMinutes(1)), Key)).Created);
    }

    [Fact]
    public async Task Preview_token_of_another_user_is_rejected()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var token = (await PreviewAsync(tools, seed, user: User(AppRoles.SistemYonetici, "user-2"))).PreviewToken;

        await RejectedAsync(PreviewRequired, () => ConfirmAsync(tools, seed, token, Key));
        await AssertNothingWrittenAsync(db);
    }

    // Gider önizlemesi gelir kaydını açmaz (girdiler aynı olsa da).
    [Fact]
    public async Task Preview_token_of_another_tool_is_rejected()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var token = (await ExpensePreviewAsync(tools, seed)).PreviewToken;

        await RejectedAsync(PreviewRequired, () => IncomeConfirmAsync(tools, seed, token, Key, categoryId: seed.ExpenseCategoryId));
        await AssertNothingWrittenAsync(db);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("date")]
    [InlineData("account")]
    [InlineData("unit")]
    [InlineData("period")]
    [InlineData("receipt")]
    [InlineData("reference")]
    [InlineData("note")]
    public async Task Preview_token_of_changed_collection_input_is_rejected(string changed)
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var user = Admin();
        var token = ((WritePreview)await tools.RecordCollectionAsync(
            user, seed.UnitId, Json("500"), $"cash:{seed.CashId}", "2026-03-05", "2025-2026", false, "DKT-1", "Not")).PreviewToken;

        Task<WriteResult> Confirm(string change) => tools.RecordCollectionAsync(
            user,
            change == "unit" ? seed.OtherUnitId : seed.UnitId,
            Json(change == "amount" ? "500.01" : "500"),
            change == "account" ? $"bank:{seed.BankId}" : $"cash:{seed.CashId}",
            change == "date" ? "2026-03-06" : "2026-03-05",
            change == "period" ? "devir" : "2025-2026",
            change == "receipt",
            change == "reference" ? "DKT-2" : "DKT-1",
            change == "note" ? "Başka not" : "Not",
            confirm: true,
            idempotency_key: Key,
            preview_token: token);

        await RejectedAsync(PreviewRequired, () => Confirm(changed));
        await AssertNothingWrittenAsync(db);
        Assert.IsType<WriteReceipt>(await Confirm("hiçbiri"));
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("date")]
    [InlineData("account")]
    [InlineData("category")]
    [InlineData("description")]
    public async Task Preview_token_of_changed_expense_input_is_rejected(string changed)
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var token = (await ExpensePreviewAsync(tools, seed)).PreviewToken;

        Task<WriteResult> Confirm(string change) => tools.CreateExpenseAsync(
            Admin(),
            change == "category" ? seed.OtherExpenseCategoryId : seed.ExpenseCategoryId,
            Json(change == "amount" ? "1250.51" : "1250.5"),
            change == "account" ? $"cash:{seed.CashId}" : $"bank:{seed.BankId}",
            change == "description" ? "Nisan faturası" : "Mart faturası",
            change == "date" ? "2026-03-06" : "2026-03-05",
            confirm: true,
            idempotency_key: Key,
            preview_token: token);

        await RejectedAsync(PreviewRequired, () => Confirm(changed));
        await AssertNothingWrittenAsync(db);
        Assert.IsType<WriteReceipt>(await Confirm("hiçbiri"));
    }

    // Aynı tutarın, hesabın ve boş alanların farklı yazımları aynı girdidir.
    [Theory]
    [InlineData("2500")]
    [InlineData("2500.00")]
    [InlineData("2500.000")]
    [InlineData("\"2.500,00\"")]
    [InlineData("\"2500,00\"")]
    [InlineData("\"₺2.500\"")]
    public async Task Differently_written_same_inputs_share_preview_and_key(string amount)
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var token = ((WritePreview)await tools.RecordCollectionAsync(
            Admin(), seed.UnitId, Json("2500"), $"cash:{seed.CashId}", "2026-03-05", "devir")).PreviewToken;

        var first = await ConfirmRawAsync(Key, "2500", $"cash:{seed.CashId}", "devir", null, null);
        var retry = await ConfirmRawAsync(Key, amount, $" CASH:{seed.CashId} ", " DEVIR ", " ", "");
        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(1, await db.Collections.CountAsync());

        var second = await ConfirmRawAsync(OtherKey, amount, $" CASH:{seed.CashId} ", " DEVIR ", " ", "");
        Assert.NotEqual(first.Id, second.Id);

        async Task<WriteReceipt> ConfirmRawAsync(
            string key, string amountJson, string account, string period, string? reference, string? note)
            => (WriteReceipt)await tools.RecordCollectionAsync(
                Admin(), seed.UnitId, Json(amountJson), account, "2026-03-05", period, false, reference, note,
                confirm: true, idempotency_key: key, preview_token: token);
    }

    // Tarih verilmeyen önizleme bugüne çözülür; kayıtta aynı gün açıkça verilebilir.
    [Fact]
    public async Task Default_date_is_resolved_before_binding_the_preview()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var preview = (WritePreview)await tools.RecordCollectionAsync(Admin(), seed.UnitId, Json("100"), $"cash:{seed.CashId}");

        var receipt = (WriteReceipt)await tools.RecordCollectionAsync(
            Admin(), seed.UnitId, Json("100"), $"cash:{seed.CashId}", preview.Date,
            confirm: true, idempotency_key: Key, preview_token: preview.PreviewToken);

        var saved = Assert.Single(await db.Collections.AsNoTracking().ToListAsync());
        Assert.Equal(receipt.Id, saved.Id);
        Assert.Equal(preview.Date, saved.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    // Saklı sonuç önizlemeden önce gelir: süresi dolmuş ya da eksik token'la tekrar deneme ilk sonucu alır.
    [Fact]
    public async Task Retry_returns_stored_result_without_valid_preview_token()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var token = (await PreviewAsync(tools, seed)).PreviewToken;
        var first = await ConfirmAsync(tools, seed, token, Key);

        Assert.Equal(first.Id, (await ConfirmAsync(tools, seed, null, Key)).Id);
        Assert.Equal(first.Id, (await ConfirmAsync(tools, seed, "gecersiz", Key)).Id);
        Assert.Equal(1, await db.Collections.CountAsync());
    }

    // Anahtar kullanıcı bazındadır: başka kullanıcının aynı anahtarı onun sonucunu döndürmez.
    [Fact]
    public async Task Same_key_of_another_user_is_independent()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var other = User(AppRoles.SistemYonetici, "user-2");
        var first = await ConfirmAsync(tools, seed, (await PreviewAsync(tools, seed)).PreviewToken, Key);

        await RejectedAsync(PreviewRequired, () => ConfirmAsync(tools, seed, null, Key, user: other));
        var second = await ConfirmAsync(tools, seed, (await PreviewAsync(tools, seed, user: other)).PreviewToken, Key, user: other);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, await db.Collections.CountAsync());
    }

    [Fact]
    public async Task Key_reuse_with_different_inputs_is_rejected()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        await ConfirmAsync(tools, seed, (await PreviewAsync(tools, seed)).PreviewToken, Key);
        var changedToken = (await PreviewAsync(tools, seed, amount: "75")).PreviewToken;

        await RejectedAsync(KeyReused, () => ConfirmAsync(tools, seed, changedToken, Key, amount: "75"));

        Assert.Equal(1, await db.Collections.CountAsync());
        Assert.True((await ConfirmAsync(tools, seed, changedToken, OtherKey, amount: "75")).Created);
    }

    // Gerçek işlemle (SQLite): yarıda kalan kayıt hiç iz bırakmaz, aynı anahtar ve önizleme yeniden kullanılabilir.
    [Theory]
    [InlineData(FailureMode.AfterWriting)]
    [InlineData(FailureMode.SaveChanges)]
    public async Task Failed_write_leaves_key_reusable_and_writes_nothing(FailureMode mode)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = CreateSqliteDb(connection);
        await db.Database.EnsureCreatedAsync();
        var seed = await SeedAsync(db);
        var auditsBefore = await db.AuditLogs.CountAsync();
        var collections = new FailingCollectionService(db) { Failure = mode };
        var tools = Tools(db, collections);
        var token = (await PreviewAsync(tools, seed, amount: "500", period: "2025-2026")).PreviewToken;

        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => ConfirmAsync(tools, seed, token, Key, amount: "500", period: "2025-2026"));

        Assert.IsNotType<McpException>(failure);
        await AssertNothingWrittenAsync(db);
        Assert.Equal(auditsBefore, await db.AuditLogs.CountAsync());
        Assert.Equal(500m, await db.DuesInstallments.AsNoTracking()
            .Where(x => x.UnitId == seed.UnitId && x.Period == "2025-2026").Select(x => x.RemainingAmount).SingleAsync());

        collections.Failure = FailureMode.None;
        var receipt = await ConfirmAsync(tools, seed, token, Key, amount: "500", period: "2025-2026");

        Assert.Equal([new AllocatedLine("2025-2026 Aidat Tipi", 500m, 0m)], receipt.Lines);
        Assert.Equal(receipt.Id, Assert.Single(await db.Collections.AsNoTracking().ToListAsync()).Id);
        Assert.Equal(1, await db.CollectionAllocations.CountAsync());
        Assert.Equal(1, await db.McpIdempotencyKeys.CountAsync());
    }

    [Fact]
    public async Task Record_collection_rejects_unknown_unit()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);

        await RejectedAsync("Daire bulunamadı.", () => Tools(db).RecordCollectionAsync(
            Admin(), 99_999, Json("100"), $"cash:{seed.CashId}"));
    }

    // Servis bozuk anahtarda sessizce "Banka" kanalına düşer; araç buna güvenmez.
    [Fact]
    public async Task Account_must_be_an_existing_active_cash_or_bank_account()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);

        foreach (var key in new[] { "", "kasa", "cash:abc", "safe:1", "cash:0", "cash:99999", "bank:99999", $"cash:{seed.InactiveCashId}", $"bank:{seed.InactiveBankId}" })
        {
            await RejectedAsync(BadAccount, () => tools.RecordCollectionAsync(Admin(), seed.UnitId, Json("100"), key));
            await RejectedAsync(BadAccount, () => tools.CreateExpenseAsync(Admin(), seed.ExpenseCategoryId, Json("100"), key, "x"));
        }

        await AssertNothingWrittenAsync(db);
    }

    // Önizlemeden sonra pasife alınan hesaba kayıt yazılmaz.
    [Fact]
    public async Task Account_deactivated_after_preview_is_rejected_on_confirm()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var token = (await PreviewAsync(tools, seed)).PreviewToken;
        (await db.CashBoxes.SingleAsync(x => x.Id == seed.CashId)).Active = false;
        await db.SaveChangesAsync();

        await RejectedAsync(BadAccount, () => ConfirmAsync(tools, seed, token, Key));

        await AssertNothingWrittenAsync(db);
    }

    [Theory]
    [InlineData("2024-2025")]
    [InlineData("2026")]
    public async Task Record_collection_rejects_period_without_open_installment(string period)
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);

        await RejectedAsync($"Bu daire için {period} döneminde açık taksit yok.", () => PreviewAsync(Tools(db), seed, period: period));
    }

    // Önizlemeden sonra dönem kapandıysa kayıt sessizce genel tahsilata dönüşmez.
    [Fact]
    public async Task Period_closed_after_preview_is_rejected_on_confirm()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);
        var token = (await PreviewAsync(tools, seed, period: "2025-2026")).PreviewToken;
        var installment = await db.DuesInstallments.SingleAsync(x => x.UnitId == seed.UnitId && x.Period == "2025-2026");
        installment.RemainingAmount = 0m;
        await db.SaveChangesAsync();

        await RejectedAsync("Bu daire için 2025-2026 döneminde açık taksit yok.",
            () => ConfirmAsync(tools, seed, token, Key, period: "2025-2026"));

        await AssertNothingWrittenAsync(db);
    }

    // Belirli döneme yapılan tahsilatın fazlası avanstır.
    [Fact]
    public async Task Record_collection_reports_advance_for_overflow()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);

        var preview = await PreviewAsync(tools, seed, amount: "650", period: "2025-2026");
        var receipt = await ConfirmAsync(tools, seed, preview.PreviewToken, Key, amount: "650", period: "2025-2026");

        Assert.Equal(150m, preview.Advance);
        Assert.Contains("₺150,00", preview.Summary);
        Assert.Equal(150m, receipt.Advance);
        Assert.Equal(preview.Lines, receipt.Lines);
    }

    [Fact]
    public async Task Receipt_requires_reference_number()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);

        foreach (var reference in new[] { null, " " })
        {
            await RejectedAsync("Makbuz için makbuz no giriniz.", () => tools.RecordCollectionAsync(
                Admin(), seed.UnitId, Json("100"), $"cash:{seed.CashId}", isReceipt: true, referenceNo: reference));
        }

        var preview = (WritePreview)await tools.RecordCollectionAsync(
            Admin(), seed.UnitId, Json("100"), $"cash:{seed.CashId}", "2026-03-05", isReceipt: true, referenceNo: "M-7", note: "Elden");
        await tools.RecordCollectionAsync(
            Admin(), seed.UnitId, Json("100"), $"cash:{seed.CashId}", "2026-03-05", isReceipt: true, referenceNo: "M-7", note: "Elden",
            confirm: true, idempotency_key: Key, preview_token: preview.PreviewToken);

        Assert.Contains("M-7", preview.Summary);
        Assert.Contains("Elden", preview.Summary);
        var saved = Assert.Single(await db.Collections.AsNoTracking().ToListAsync());
        Assert.True(saved.IsReceipt);
        Assert.Equal("M-7", saved.ReferenceNo);
        Assert.Equal("Elden", saved.Note);
    }

    // Sütun sınırını aşan metin önizlemede anlaşılır hata verir; kayıtta genel hataya dönüşmez.
    [Fact]
    public async Task Too_long_texts_are_rejected_at_preview()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);

        await RejectedAsync("referenceNo en fazla 80 karakter olabilir.", () => tools.RecordCollectionAsync(
            Admin(), seed.UnitId, Json("100"), $"cash:{seed.CashId}", referenceNo: new string('x', 81)));
        await RejectedAsync("note en fazla 250 karakter olabilir.", () => tools.RecordCollectionAsync(
            Admin(), seed.UnitId, Json("100"), $"cash:{seed.CashId}", note: new string('x', 251)));
        await RejectedAsync("description en fazla 250 karakter olabilir.", () => tools.CreateExpenseAsync(
            Admin(), seed.ExpenseCategoryId, Json("100"), $"cash:{seed.CashId}", new string('x', 251)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("\"abc\"")]
    [InlineData("1000000000")]
    public async Task Bad_amount_is_rejected(string amount)
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);

        await RejectedAsync("Geçerli bir tutar giriniz.", () => PreviewAsync(Tools(db), seed, amount: amount));
        await RejectedAsync("Tutar en fazla 2 ondalık basamak içerebilir.", () => PreviewAsync(Tools(db), seed, amount: "10.555"));
    }

    // Servisin Türkçe hata mesajı Claude'a ulaşır.
    [Fact]
    public async Task Service_validation_message_reaches_the_caller()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);

        await RejectedAsync("Bu daire için aktif bir aidat grubu bulunamadı.", () => Tools(db).RecordCollectionAsync(
            Admin(), seed.GrouplessUnitId, Json("100"), $"cash:{seed.CashId}"));
    }

    // EF/çalışma zamanı hataları (InvalidOperationException olsa da) istemciye metniyle gitmez.
    [Fact]
    public async Task Unexpected_errors_are_not_turned_into_client_messages()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db, new FailingCollectionService(db) { Failure = FailureMode.FrameworkError });

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => PreviewAsync(tools, seed));

        Assert.IsNotType<McpException>(ex);
    }

    [Fact]
    public void Write_tools_are_annotated_as_non_destructive_writes()
    {
        var tools = typeof(WriteTools).GetMethods()
            .Select(x => x.GetCustomAttribute<McpServerToolAttribute>())
            .OfType<McpServerToolAttribute>()
            .ToDictionary(x => x.Name!);

        Assert.Equal(["create_expense", "create_income", "record_collection"], tools.Keys.Order());
        Assert.All(tools.Values, x =>
        {
            Assert.False(x.ReadOnly);
            Assert.False(x.Destructive);
            Assert.False(x.Idempotent);
        });
    }

    public enum FailureMode { None, AfterWriting, SaveChanges, FrameworkError }

    // Gerçek servisi sarar; kaydın ortasında hata üretir.
    private sealed class FailingCollectionService(ApplicationDbContext db) : ICollectionService
    {
        private readonly CollectionService _inner = new(db);

        public FailureMode Failure { get; set; }

        public Task<List<Collection>> GetAllAsync() => _inner.GetAllAsync();

        public Task<Collection?> GetByIdAsync(int id) => _inner.GetByIdAsync(id);

        public async Task<int> CreateAsync(CollectionCreateViewModel model)
        {
            if (Failure == FailureMode.SaveChanges)
            {
                // Olmayan daireye yabancı anahtar: SaveChanges patlar, satır izlemede kalır.
                db.CollectionAllocations.Add(new CollectionAllocation { CollectionId = 99_999, AppliedAmount = 1m });
            }

            var id = await _inner.CreateAsync(model);
            return Failure == FailureMode.AfterWriting ? throw new TimeoutException("Host=gizli;Password=gizli") : id;
        }

        public Task UpdateAsync(int id, CollectionCreateViewModel model) => _inner.UpdateAsync(id, model);

        public Task DeleteAsync(int id) => _inner.DeleteAsync(id);

        public Task<CollectionAllocationPlan> PlanAllocationAsync(CollectionCreateViewModel model)
            => Failure == FailureMode.FrameworkError
                ? Task.FromResult(Enumerable.Empty<CollectionAllocationPlan>().First())
                : _inner.PlanAllocationAsync(model);

        public Task<CollectionCreateViewModel> BuildForUnitAsync(int unitId, decimal amount, DateTime date, string? accountKey,
            string? period, bool isReceipt, string? referenceNo, string? note)
            => _inner.BuildForUnitAsync(unitId, amount, date, accountKey, period, isReceipt, referenceNo, note);
    }

    private sealed record Seed(
        int UnitId, int OtherUnitId, int GrouplessUnitId, int CashId, int InactiveCashId, int BankId, int InactiveBankId,
        int ExpenseCategoryId, int OtherExpenseCategoryId, int IncomeCategoryId, int InactiveCategoryId);

    // A-5: devir borcu 300, açık taksitler 2025-2026 (500) ve 2026-2027 (500).
    private static async Task<Seed> SeedAsync(ApplicationDbContext db)
    {
        var site = new Site { Name = "Site" };
        var block = new Block { Site = site, Name = "A" };
        var unit = new Unit { Block = block, UnitNo = "5", OwnerName = "Şahin", OpeningBalance = -300m, OpeningBalanceDate = Utc(2025, 5, 26) };
        var other = new Unit { Block = block, UnitNo = "6", OwnerName = "Yılmaz" };
        var groupless = new Unit { Block = block, UnitNo = "7", OwnerName = "Grupsuz" };
        var duesType = new DuesType { Name = "Aidat Tipi", Amount = 500m, Active = true };
        var group = new BillingGroup { Name = "Grup", DuesType = duesType, EffectiveStartPeriod = "2025-2026", Active = true };
        var cash = new CashBox { Name = "Ana Kasa", OpeningBalanceDate = Utc(2025, 1, 1) };
        var inactiveCash = new CashBox { Name = "Eski Kasa", OpeningBalanceDate = Utc(2025, 1, 1), Active = false };
        var bank = new BankAccount { Name = "İş Bankası", Branch = "Merkez", OpeningBalanceDate = Utc(2025, 1, 1) };
        var inactiveBank = new BankAccount { Name = "Eski Banka", OpeningBalanceDate = Utc(2025, 1, 1), Active = false };
        var expense = new IncomeExpenseCategory { Name = "Elektrik", Type = CategoryTypeHelper.Gider };
        var otherExpense = new IncomeExpenseCategory { Name = "Su", Type = CategoryTypeHelper.Gider };
        var income = new IncomeExpenseCategory { Name = "Kira", Type = CategoryTypeHelper.Gelir };
        var inactive = new IncomeExpenseCategory { Name = "Eski", Type = CategoryTypeHelper.Gider, Active = false };
        db.AddRange(site, block, unit, other, groupless, duesType, group, cash, inactiveCash, bank, inactiveBank,
            expense, otherExpense, income, inactive);
        foreach (var member in new[] { unit, other })
        {
            db.Add(new BillingGroupUnit { BillingGroup = group, Unit = member, StartPeriod = "2025-2026" });
        }

        foreach (var (period, year) in new[] { ("2025-2026", 2025), ("2026-2027", 2026) })
        {
            db.Add(new DuesInstallment
            {
                Unit = unit, BillingGroup = group, Period = period, AccrualDate = Utc(year, 8, 1), DueDate = Utc(year, 8, 1),
                Amount = 500m, RemainingAmount = 500m, Status = InstallmentStatus.Open
            });
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return new Seed(unit.Id, other.Id, groupless.Id, cash.Id, inactiveCash.Id, bank.Id, inactiveBank.Id,
            expense.Id, otherExpense.Id, income.Id, inactive.Id);
    }

    private static async Task<CollectionAllocationPlan> PlanAsync(ApplicationDbContext db, Seed seed, decimal amount, string period)
    {
        var service = new CollectionService(db);
        return await service.PlanAllocationAsync(await service.BuildForUnitAsync(
            seed.UnitId, amount, Utc(2026, 3, 5), $"cash:{seed.CashId}", period, false, null, null));
    }

    private static async Task<WritePreview> PreviewAsync(
        WriteTools tools, Seed seed, string amount = "100", string? period = null, ClaimsPrincipal? user = null)
        => Assert.IsType<WritePreview>(await tools.RecordCollectionAsync(
            user ?? Admin(), seed.UnitId, Json(amount), $"cash:{seed.CashId}", "2026-03-05", period));

    private static async Task<WriteReceipt> ConfirmAsync(
        WriteTools tools, Seed seed, string? token, string? key, string amount = "100", string? period = null, ClaimsPrincipal? user = null)
        => Assert.IsType<WriteReceipt>(await tools.RecordCollectionAsync(
            user ?? Admin(), seed.UnitId, Json(amount), $"cash:{seed.CashId}", "2026-03-05", period,
            confirm: true, idempotency_key: key, preview_token: token));

    private static async Task<WritePreview> ExpensePreviewAsync(
        WriteTools tools, Seed seed, int? categoryId = null, ClaimsPrincipal? user = null)
        => Assert.IsType<WritePreview>(await tools.CreateExpenseAsync(
            user ?? Admin(), categoryId ?? seed.ExpenseCategoryId, Json("1250.5"), $"bank:{seed.BankId}", "Mart faturası", "2026-03-05"));

    private static async Task<WriteReceipt> ExpenseConfirmAsync(WriteTools tools, Seed seed, string? token, string? key)
        => Assert.IsType<WriteReceipt>(await tools.CreateExpenseAsync(
            Admin(), seed.ExpenseCategoryId, Json("\"1.250,50\""), $"bank:{seed.BankId}", "Mart faturası", "2026-03-05",
            confirm: true, idempotency_key: key, preview_token: token));

    private static async Task<WritePreview> IncomePreviewAsync(
        WriteTools tools, Seed seed, int? categoryId = null, ClaimsPrincipal? user = null)
        => Assert.IsType<WritePreview>(await tools.CreateIncomeAsync(
            user ?? Admin(), categoryId ?? seed.IncomeCategoryId, Json("1250.5"), $"bank:{seed.BankId}", "Mart faturası", "2026-03-05"));

    private static async Task<WriteReceipt> IncomeConfirmAsync(
        WriteTools tools, Seed seed, string? token, string? key, int? categoryId = null)
        => Assert.IsType<WriteReceipt>(await tools.CreateIncomeAsync(
            Admin(), categoryId ?? seed.IncomeCategoryId, Json("1250.5"), $"bank:{seed.BankId}", "Mart faturası", "2026-03-05",
            confirm: true, idempotency_key: key, preview_token: token));

    private static async Task RejectedAsync(string message, Func<Task> call)
        => Assert.Equal(message, (await Assert.ThrowsAsync<McpException>(call)).Message);

    private static async Task AssertNothingWrittenAsync(ApplicationDbContext db)
    {
        Assert.Equal(0, await db.Collections.CountAsync());
        Assert.Equal(0, await db.CollectionAllocations.CountAsync());
        Assert.Equal(0, await db.LedgerTransactions.CountAsync());
        Assert.Equal(0, await db.McpIdempotencyKeys.CountAsync());
    }

    private static WriteTools Tools(ApplicationDbContext db, ICollectionService? collections = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OAuth:Issuer"] = "https://localhost/" })
            .Build();
        return new WriteTools(
            db,
            new PermissionService(db, new MemoryCache(new MemoryCacheOptions())),
            collections ?? new CollectionService(db),
            new LedgerEntryService(db),
            new DuesLedgerRowService(db),
            LedgerOf(db),
            new McpIdempotencyStore(db),
            new McpPreviewTokens(DataProtection),
            new McpLinks(config));
    }

    private static UnitLedgerService LedgerOf(ApplicationDbContext db) => new(db, new DuesLedgerRowService(db));

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ApplicationDbContext CreateSqliteDb(SqliteConnection connection)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);

    private static ClaimsPrincipal Admin() => User(AppRoles.SistemYonetici);

    private static ClaimsPrincipal User(string role, string userId = "user-1")
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, role)], "Test"));

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);
}
