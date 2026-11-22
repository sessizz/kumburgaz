using System.Security.Claims;
using Kumburgaz.Web.Controllers;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Mcp;
using Kumburgaz.Web.Mcp.Tools;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class McpFinanceToolsTests
{
    [Fact]
    public async Task Site_summary_matches_cash_bank_balances()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);

        var summary = await Tools(db).SiteSummaryAsync(Admin());

        var balances = await CashBankBalanceHelper.BuildAsync(db);
        Assert.Equal(balances.Where(x => x.Type == "cash").Sum(x => x.Balance), summary.CashTotal);
        Assert.Equal(balances.Where(x => x.Type == "bank").Sum(x => x.Balance), summary.BankTotal);
        Assert.Equal(balances.Count, summary.Accounts.Count);
        Assert.Contains(summary.Accounts, x => x.Key == $"cash:{seed.CashId}" && x.Name == "Ana Kasa" && x.Balance == 450m);
        Assert.Contains(summary.Accounts, x => x.Key == $"bank:{seed.BankId}" && x.Name == "İş Bankası - Merkez");
    }

    // Panel ekranının (HomeController.Index) model değerleriyle aynı rakamlar.
    [Fact]
    public async Task Site_summary_matches_panel_figures()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var monthStart = DashboardFiguresService.MonthStartUtc(DateTime.UtcNow);
        db.Collections.Add(new Collection
        {
            UnitId = seed.UnitId, BillingGroupId = seed.GroupId, Date = monthStart, Amount = 1_000m,
            PaymentChannel = PaymentChannel.Cash, CashBoxId = seed.CashId
        });
        db.Collections.Add(new Collection
        {
            UnitId = seed.UnitId, BillingGroupId = seed.GroupId, Date = monthStart.AddMonths(-1), Amount = 7_000m,
            PaymentChannel = PaymentChannel.Cash, CashBoxId = seed.CashId
        });
        db.LedgerTransactions.AddRange(
            Ledger(seed.IncomeCategoryId, monthStart, 200m, seed.CashId),
            Ledger(seed.ExpenseCategoryId, monthStart, 5_000m, seed.CashId),
            Ledger(seed.ExpenseCategoryId, monthStart.AddMonths(-1), 999m, seed.CashId),
            new LedgerTransaction { Date = monthStart, IsTransfer = true, Amount = 300m, CashBoxId = seed.CashId });
        await db.SaveChangesAsync();

        var summary = await Tools(db).SiteSummaryAsync(Admin());
        var vm = (DashboardViewModel)((ViewResult)await Home(db).Index()).Model!;

        Assert.True(summary.OverdueDebtTotal > 0m);
        Assert.Equal(vm.OverdueDebt, summary.OverdueDebtTotal);
        Assert.Equal(vm.MonthCollections, summary.CollectionsThisMonth);
        Assert.Equal(1_000m, summary.CollectionsThisMonth);
        Assert.Equal(vm.CashBankBalance, summary.CashTotal + summary.BankTotal);
        Assert.Equal(vm.Cashflow[^1].Expense, summary.ExpensesThisMonth);
        Assert.Equal(5_000m, summary.ExpensesThisMonth);
        // Panel "diğer gelir"i yalnız "Bu ay nakit eksi" uyarısında gösterir: tahsilat + diğer gelir.
        Assert.Equal(200m, summary.IncomeThisMonth);
        Assert.Contains($"{summary.CollectionsThisMonth + summary.IncomeThisMonth:N2}", vm.Alerts.Single(x => x.Title == "Bu ay nakit eksi").Message);
        var duesDebt = DuesDebtSummaryHelper.Build(await new ReportingService(db, Ledger(db), new DuesLedgerRowService(db)).GetDuesDebtReportAsync(new())).TotalDebt;
        Assert.Equal(duesDebt, summary.DuesDebtTotal);
    }

    [Fact]
    public async Task Collections_filters_by_date_and_orders_newest_first()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);

        var result = await tools.CollectionsAsync(Admin(), from: "2025-03-01", to: "2025-03-31");

        Assert.Equal([seed.March10, seed.March10Late], result.Rows.Select(x => x.Id).Reverse().ToList());
        Assert.Equal(2, result.Count);
        Assert.Equal(800m, result.Total);
        var newest = result.Rows[0];
        Assert.Equal("2025-03-10", newest.Date);
        Assert.Equal("A-5", newest.Unit);
        Assert.Equal("Ana Kasa", newest.Account);
        Assert.Equal($"https://localhost/Collections/Edit/{newest.Id}", newest.Link);

        var byUnit = await tools.CollectionsAsync(Admin(), unitId: seed.UnitId + 1000);
        Assert.Empty(byUnit.Rows);
        var byBank = await tools.CollectionsAsync(Admin(), accountKey: $"bank:{seed.BankId}");
        Assert.Equal([seed.BankCollection], byBank.Rows.Select(x => x.Id));
        Assert.Equal("İş Bankası - Merkez", byBank.Rows[0].Account);
    }

    // to günü bütün gün dahildir (kayıt 14:00 UTC damgalı).
    [Fact]
    public async Task Collections_to_day_is_inclusive_and_from_day_starts_at_midnight()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);

        var onlyThatDay = await Tools(db).CollectionsAsync(Admin(), from: "2025-03-10", to: "2025-03-10");
        var before = await Tools(db).CollectionsAsync(Admin(), to: "2025-03-09");

        Assert.Equal(2, onlyThatDay.Count);
        Assert.DoesNotContain(before.Rows, x => x.Id == seed.March10 || x.Id == seed.March10Late);
    }

    [Fact]
    public async Task Collections_limit_keeps_total_and_count_of_all_matching_rows()
    {
        await using var db = CreateDb();
        await SeedAsync(db);

        var all = await Tools(db).CollectionsAsync(Admin());
        var limited = await Tools(db).CollectionsAsync(Admin(), limit: 1);

        Assert.Single(limited.Rows);
        Assert.Equal(all.Count, limited.Count);
        Assert.Equal(all.Total, limited.Total);
        Assert.Equal(3, limited.Count);
        Assert.Equal(1_100m, limited.Total);
    }

    [Fact]
    public async Task Collections_rejects_bad_input()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var tools = Tools(db);

        var key = await Assert.ThrowsAsync<McpException>(() => tools.CollectionsAsync(Admin(), accountKey: "safe:1"));
        var range = await Assert.ThrowsAsync<McpException>(() => tools.CollectionsAsync(Admin(), from: "2025-04-01", to: "2025-03-01"));

        Assert.Equal("Geçerli bir kasa/banka hesabı belirtin (cash:{id} ya da bank:{id}).", key.Message);
        Assert.Equal("from, to'dan sonra olamaz.", range.Message);
    }

    [Fact]
    public async Task Cash_bank_accounts_lists_keys_balances_and_links()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);

        var rows = await Tools(db).CashBankAccountsAsync(Admin());

        var cash = Assert.Single(rows, x => x.Type == "cash");
        Assert.Equal($"cash:{seed.CashId}", cash.Key);
        Assert.Equal(450m, cash.Balance);
        Assert.Equal($"https://localhost/CashBank/CashBox/{seed.CashId}", cash.Link);
        var bank = Assert.Single(rows, x => x.Type == "bank");
        Assert.Equal($"bank:{seed.BankId}", bank.Key);
        Assert.Equal($"https://localhost/CashBank/Bank/{seed.BankId}", bank.Link);
    }

    [Fact]
    public async Task Cash_bank_transactions_closing_equals_account_balance()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var balances = await CashBankBalanceHelper.BuildAsync(db);

        var result = await Tools(db).CashBankTransactionsAsync(Admin(), $"cash:{seed.CashId}");

        Assert.Equal(balances.Single(x => x.Type == "cash").Balance, result.Closing);
        Assert.Equal(100m, result.Opening);
        Assert.Equal("Ana Kasa", result.Account);
        Assert.Equal(5, result.Count);
        // En yeni önce; yürüyen bakiye tüm geçmişi kapsar.
        Assert.Equal(["2025-04-05", "2025-03-25", "2025-03-20", "2025-03-10", "2025-03-10"], result.Rows.Select(x => x.Date));
        Assert.Equal(450m, result.Rows[0].Balance);
        Assert.Equal(50m, result.Rows[0].Inflow);
        Assert.Equal(0m, result.Rows[0].Outflow);
        Assert.Equal(300m, result.Rows[1].Outflow);
        Assert.Equal(400m, result.Rows[1].Balance);
        Assert.Equal(700m, result.Rows[2].Balance);
        Assert.Equal($"https://localhost/CashBank/CashBox/{seed.CashId}", result.Link);
    }

    // Filtreli aralığın kapanışı, aralık sonundaki bakiyedir (Excel dışa aktarımıyla aynı anlam).
    [Fact]
    public async Task Cash_bank_transactions_closing_of_filtered_range_is_balance_at_range_end()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);

        var mid = await tools.CashBankTransactionsAsync(Admin(), $"cash:{seed.CashId}", from: "2025-03-15", to: "2025-03-31");
        var toFirstDay = await tools.CashBankTransactionsAsync(Admin(), $"cash:{seed.CashId}", to: "2025-03-10");
        var emptyRange = await tools.CashBankTransactionsAsync(Admin(), $"cash:{seed.CashId}", from: "2025-03-26", to: "2025-04-04");

        Assert.Equal(400m, mid.Closing);
        Assert.Equal(100m, mid.Opening);
        Assert.Equal(["2025-03-25", "2025-03-20"], mid.Rows.Select(x => x.Date));
        // to günü bütün gün dahil: 14:00 UTC'deki tahsilat da sayılır.
        Assert.Equal(900m, toFirstDay.Closing);
        Assert.Equal(2, toFirstDay.Count);
        Assert.Empty(emptyRange.Rows);
        Assert.Equal(400m, emptyRange.Closing);
    }

    [Fact]
    public async Task Cash_bank_transactions_limit_keeps_count_and_validates_input()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);

        var limited = await tools.CashBankTransactionsAsync(Admin(), $"cash:{seed.CashId}", limit: 2);
        Assert.Equal(2, limited.Rows.Count);
        Assert.Equal(5, limited.Count);

        foreach (var key in new[] { "cash:999", "bank:abc", "kasa", "cash:0" })
        {
            var ex = await Assert.ThrowsAsync<McpException>(() => tools.CashBankTransactionsAsync(Admin(), key));
            Assert.Equal("Geçerli bir kasa/banka hesabı belirtin (cash:{id} ya da bank:{id}).", ex.Message);
        }

        var range = await Assert.ThrowsAsync<McpException>(
            () => tools.CashBankTransactionsAsync(Admin(), $"cash:{seed.CashId}", from: "2025-04-01", to: "2025-03-01"));
        Assert.Equal("from, to'dan sonra olamaz.", range.Message);
    }

    [Fact]
    public async Task Ledger_filters_expenses_by_category()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db);

        var expenses = await tools.LedgerAsync(Admin(), kind: "expense");
        var byCategory = await tools.LedgerAsync(Admin(), categoryId: seed.ExpenseCategoryId);
        var incomes = await tools.LedgerAsync(Admin(), kind: "income");

        // Transfer (kategorisiz ve kategorili) listeye girmez.
        Assert.Equal(["2025-03-20", "2025-02-01"], expenses.Rows.Select(x => x.Date));
        Assert.All(expenses.Rows, x => Assert.Equal("expense", x.Kind));
        Assert.Equal(expenses.Rows.Select(x => x.Id), byCategory.Rows.Select(x => x.Id));
        Assert.Equal(1_200m, expenses.Total);
        Assert.Equal("Elektrik", expenses.Rows[0].Category);
        Assert.Equal(200m, expenses.Rows[0].Amount);
        Assert.Equal("Ana Kasa", expenses.Rows[0].Account);
        Assert.Equal($"https://localhost/Ledger/Edit/{expenses.Rows[0].Id}", expenses.Rows[0].Link);
        Assert.Equal(["2025-04-05"], incomes.Rows.Select(x => x.Date));
        Assert.Equal("income", incomes.Rows[0].Kind);
    }

    [Fact]
    public async Task Ledger_limit_keeps_total_and_count_and_to_day_is_inclusive()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var tools = Tools(db);

        var all = await tools.LedgerAsync(Admin());
        var limited = await tools.LedgerAsync(Admin(), limit: 1);
        var toDay = await tools.LedgerAsync(Admin(), from: "2025-03-20", to: "2025-03-20");

        Assert.Equal(3, all.Count);
        Assert.Equal(1_250m, all.Total);
        Assert.Single(limited.Rows);
        Assert.Equal(all.Count, limited.Count);
        Assert.Equal(all.Total, limited.Total);
        Assert.Equal(1, toDay.Count);
        Assert.Equal(200m, toDay.Total);
    }

    [Fact]
    public async Task Ledger_rejects_bad_kind_and_range()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var tools = Tools(db);

        var kind = await Assert.ThrowsAsync<McpException>(() => tools.LedgerAsync(Admin(), kind: "transfer"));
        var range = await Assert.ThrowsAsync<McpException>(() => tools.LedgerAsync(Admin(), from: "2025-04-01", to: "2025-03-01"));

        Assert.Equal("kind \"income\" ya da \"expense\" olmalı.", kind.Message);
        Assert.Equal("from, to'dan sonra olamaz.", range.Message);
    }

    // Personel yetki matrisinde yalnız Panel ve Raporlar var; her araç kendi modülünü ister.
    [Fact]
    public async Task Each_tool_requires_its_own_module()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        // Yalnız "Daireler" yetkisi olan kullanıcı hiçbirini kullanamaz.
        var tools = Tools(db, AppRoles.Personel, AppModules.Daireler);
        var user = User(AppRoles.Personel);

        await DeniedAsync(() => tools.SiteSummaryAsync(user));
        await DeniedAsync(() => tools.CollectionsAsync(user));
        await DeniedAsync(() => tools.CashBankAccountsAsync(user));
        await DeniedAsync(() => tools.CashBankTransactionsAsync(user, $"cash:{seed.CashId}"));
        await DeniedAsync(() => tools.LedgerAsync(user));

        // Yetki yalnız ilgili modülü açar.
        var panelOnly = Tools(db, AppRoles.Personel, AppModules.Panel);
        Assert.NotNull(await panelOnly.SiteSummaryAsync(user));
        await DeniedAsync(() => panelOnly.CollectionsAsync(user));
    }

    private static async Task DeniedAsync(Func<Task> call)
    {
        var ex = await Assert.ThrowsAsync<McpException>(call);
        Assert.Contains("yetkiniz yok", ex.Message);
    }

    private sealed record Seed(
        int UnitId, int GroupId, int CashId, int BankId, int ExpenseCategoryId, int IncomeCategoryId,
        int March10, int March10Late, int BankCollection);

    // Kasa (sırayla): açılış 100 (01-01); +300 tahsilat 03-10 09:00 (bakiye 400); +500 tahsilat 03-10 14:00 (900);
    // -200 gider 03-20 (700); -300 transfer çıkışı 03-25 (400); +50 gelir 04-05 (450).
    // Banka: +300 tahsilat 02-01, -1000 gider 02-01 (bakiye -700).
    private static async Task<Seed> SeedAsync(ApplicationDbContext db)
    {
        var site = new Site { Name = "Site" };
        var block = new Block { Site = site, Name = "A" };
        var unit = new Unit { Block = block, UnitNo = "5", OwnerName = "Şahin", OpeningBalance = -300m, OpeningBalanceDate = Utc(2025, 7, 1) };
        var duesType = new DuesType { Name = "Aidat", Amount = 1_000m, Active = true };
        var group = new BillingGroup { Name = "Grup", DuesType = duesType, EffectiveStartPeriod = "2025-2026", Active = true };
        var cash = new CashBox { Name = "Ana Kasa", OpeningBalance = 100m, OpeningBalanceDate = Utc(2025, 1, 1) };
        var bank = new BankAccount { Name = "İş Bankası", Branch = "Merkez", OpeningBalance = 0m, OpeningBalanceDate = Utc(2025, 1, 1) };
        var expense = new IncomeExpenseCategory { Name = "Elektrik", Type = CategoryTypeHelper.Gider, Active = true };
        var income = new IncomeExpenseCategory { Name = "Kira", Type = CategoryTypeHelper.Gelir, Active = true };
        db.AddRange(site, block, unit, duesType, group, cash, bank, expense, income);
        db.BillingGroupUnits.Add(new BillingGroupUnit { BillingGroup = group, Unit = unit, StartPeriod = "2025-2026" });
        await db.SaveChangesAsync();

        // Vadesi geçmiş aidat (Panel'in "vadesi geçmiş borç" kolu).
        db.DuesInstallments.Add(new DuesInstallment
        {
            UnitId = unit.Id, BillingGroupId = group.Id, Period = "2025-2026",
            AccrualDate = Utc(2025, 8, 1), DueDate = Utc(2025, 9, 1), Amount = 1_000m, RemainingAmount = 1_000m,
            Status = InstallmentStatus.Open
        });

        var late = new Collection
        {
            UnitId = unit.Id, BillingGroupId = group.Id, Date = Utc(2025, 3, 10).AddHours(14), Amount = 500m,
            PaymentChannel = PaymentChannel.Cash, CashBoxId = cash.Id
        };
        var early = new Collection
        {
            UnitId = unit.Id, BillingGroupId = group.Id, Date = Utc(2025, 3, 10).AddHours(9), Amount = 300m,
            PaymentChannel = PaymentChannel.Cash, CashBoxId = cash.Id
        };
        var bankCollection = new Collection
        {
            UnitId = unit.Id, BillingGroupId = group.Id, Date = Utc(2025, 2, 1), Amount = 300m,
            PaymentChannel = PaymentChannel.Bank, BankAccountId = bank.Id
        };
        db.Collections.AddRange(late, early, bankCollection);
        db.LedgerTransactions.AddRange(
            Ledger(expense.Id, Utc(2025, 3, 20), 200m, cash.Id),
            Ledger(expense.Id, Utc(2025, 2, 1), 1_000m, null, bank.Id),
            Ledger(income.Id, Utc(2025, 4, 5), 50m, cash.Id),
            new LedgerTransaction { Date = Utc(2025, 3, 25), IsTransfer = true, TransferIsIncoming = false, Amount = 300m, CashBoxId = cash.Id });
        await db.SaveChangesAsync();
        return new Seed(unit.Id, group.Id, cash.Id, bank.Id, expense.Id, income.Id, early.Id, late.Id, bankCollection.Id);
    }

    private static LedgerTransaction Ledger(int categoryId, DateTime date, decimal amount, int? cashId, int? bankId = null)
        => new()
        {
            Date = date, IncomeExpenseCategoryId = categoryId, Amount = amount,
            PaymentChannel = cashId.HasValue ? PaymentChannel.Cash : PaymentChannel.Bank,
            CashBoxId = cashId, BankAccountId = bankId
        };

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static UnitLedgerService Ledger(ApplicationDbContext db) => new(db, new DuesLedgerRowService(db));

    // role verilirse o role yalnız belirtilen modüller için görüntüleme yetkisi tohumlanır.
    private static FinanceTools Tools(ApplicationDbContext db, string? role = null, params string[] viewModules)
    {
        if (role is not null)
        {
            db.RolePermissions.AddRange(viewModules.Select(module =>
                new RolePermission { RoleName = role, Module = module, CanView = true }));
            db.SaveChanges();
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OAuth:Issuer"] = "https://localhost/" })
            .Build();
        var rows = new DuesLedgerRowService(db);
        return new FinanceTools(
            db,
            new PermissionService(db, new MemoryCache(new MemoryCacheOptions())),
            new ReportingService(db, Ledger(db), rows),
            new DashboardFiguresService(db, rows),
            new CashBankDetailService(db, rows),
            new McpLinks(config));
    }

    private static HomeController Home(ApplicationDbContext db)
    {
        var rows = new DuesLedgerRowService(db);
        var backupDir = Path.Combine(Path.GetTempPath(), "kumburgaz-test-" + Guid.NewGuid().ToString("N"));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Backups:Directory"] = backupDir })
            .Build();
        return new HomeController(
            db,
            new ExpenseForecastService(db),
            new BackupService(config, new TestEnvironment(backupDir), NullLogger<BackupService>.Instance),
            rows,
            new DashboardFiguresService(db, rows))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            Url = new StubUrlHelper()
        };
    }

    private static ClaimsPrincipal Admin() => User(AppRoles.SistemYonetici);

    private static ClaimsPrincipal User(string role)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test"));

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Test";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class StubUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext actionContext) => "/";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? RouteUrl(UrlRouteContext routeContext) => "/";
        public string? Link(string? routeName, object? values) => "/";
    }
}
