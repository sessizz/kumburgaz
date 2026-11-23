using System.Security.Claims;
using Kumburgaz.Web.Controllers;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Mcp;
using Kumburgaz.Web.Mcp.Tools;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class McpUnitToolsTests
{
    [Theory]
    [InlineData("A-5")]
    [InlineData("a blok 5")]
    [InlineData("A5")]
    public async Task Search_finds_unit_by_block_and_number_variants(string query)
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);

        var results = await Tools(db).SearchAsync(Admin(), query);

        var first = results[0];
        Assert.Equal("unit", first.Type);
        Assert.Equal(seed.A5.Id, first.Id);
        Assert.Equal("A-5", first.Title);
        Assert.Equal($"https://localhost/Units/Detail/{seed.A5.Id}", first.Link);
    }

    // Seed'deki blok adları "A Blok" biçimindedir; ayraçsız birleşince "Blok5" kelimesi düşmemeli.
    [Theory]
    [InlineData("A-5")]
    [InlineData("a blok 5")]
    [InlineData("A5")]
    [InlineData("A Blok-5")]
    public async Task Search_finds_unit_in_block_named_with_blok_suffix(string query)
    {
        await using var db = CreateDb();
        var site = new Site { Name = "Site" };
        var block = new Block { Site = site, Name = "A Blok" };
        var unit = new Unit { Block = block, UnitNo = "5" };
        db.AddRange(site, block, unit);
        await db.SaveChangesAsync();

        var results = await Tools(db).SearchAsync(Admin(), query);

        Assert.Equal(unit.Id, Assert.Single(results).Id);
    }

    // Birleştirmede bileşenler pasif olur, "1+2" numaralı aktif daire açılır.
    [Fact]
    public async Task Search_finds_combined_unit_by_members_and_marks_passive_units()
    {
        await using var db = CreateDb();
        var site = new Site { Name = "Site" };
        var block = new Block { Site = site, Name = "A Blok" };
        var one = new Unit { Block = block, UnitNo = "1", Active = false };
        var two = new Unit { Block = block, UnitNo = "2", Active = false };
        var combined = new Unit { Block = block, UnitNo = "1+2", IsCombined = true };
        db.AddRange(site, block, one, two, combined);
        await db.SaveChangesAsync();
        db.CombinedUnitMembers.AddRange(
            new CombinedUnitMember { CombinedUnitId = combined.Id, ComponentUnitId = one.Id },
            new CombinedUnitMember { CombinedUnitId = combined.Id, ComponentUnitId = two.Id });
        await db.SaveChangesAsync();
        var tools = Tools(db);

        var results = await tools.SearchAsync(Admin(), "A-2");

        Assert.Equal([combined.Id, two.Id], results.Select(x => x.Id));
        Assert.DoesNotContain("Pasif", results[0].Subtitle);
        Assert.Contains("Pasif (birleştirilmiş daire)", results[1].Subtitle);
        Assert.True((await tools.GetUnitAsync(Admin(), combined.Id)).Active);
        Assert.False((await tools.GetUnitAsync(Admin(), two.Id)).Active);
    }

    // Elle pasife alınan daire birleştirilmiş değildir.
    [Fact]
    public async Task Search_marks_manually_deactivated_unit_as_plain_passive()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        seed.A5.Active = false;
        await db.SaveChangesAsync();

        var result = Assert.Single(await Tools(db).SearchAsync(Admin(), "A-5"), x => x.Id == seed.A5.Id);

        Assert.StartsWith("Pasif, ", result.Subtitle);
        Assert.DoesNotContain("birleştirilmiş", result.Subtitle);
    }

    // Çıplak daire no gürültü yapmaz: "15" yalnız 15 numaralı daireyi ve 15 ile başlayanları getirir.
    [Fact]
    public async Task Search_ranks_exact_unit_number_before_prefix_and_skips_contains()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);

        var results = await Tools(db).SearchAsync(Admin(), "15");

        Assert.Equal([seed.A15.Id, seed.A150.Id], results.Where(x => x.Type == "unit").Select(x => x.Id));
        Assert.DoesNotContain(results, x => x.Id == seed.A5.Id);
        Assert.DoesNotContain(results, x => x.Id == seed.B115.Id);
    }

    [Fact]
    public async Task Search_returns_at_most_20_results()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var block = await db.Blocks.FirstAsync(x => x.Name == "A");
        db.Units.AddRange(Enumerable.Range(0, 30).Select(i => new Unit { BlockId = block.Id, UnitNo = $"7{i}" }));
        await db.SaveChangesAsync();

        var results = await Tools(db).SearchAsync(Admin(), "a7");

        Assert.Equal(20, results.Count);
    }

    [Theory]
    [InlineData("sahin")]
    [InlineData("SAHİN")]
    [InlineData("Şahin Yılmaz")]
    public async Task Search_finds_owner_by_name_without_turkish_letters(string query)
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);

        var results = await Tools(db).SearchAsync(Admin(), query);

        var unit = Assert.Single(results, x => x.Type == "unit");
        Assert.Equal(seed.A5.Id, unit.Id);
        Assert.Contains("Şahin Yılmaz", unit.Subtitle);
    }

    [Fact]
    public async Task Search_finds_tenant_by_name()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);

        var results = await Tools(db).SearchAsync(Admin(), "kemal");

        Assert.Equal(seed.A5.Id, Assert.Single(results, x => x.Type == "unit").Id);
    }

    [Theory]
    [InlineData("1")]
    [InlineData(" a ")]
    [InlineData("")]
    public async Task Search_requires_two_characters(string query)
    {
        await using var db = CreateDb();
        await SeedAsync(db);

        var ex = await Assert.ThrowsAsync<McpException>(() => Tools(db).SearchAsync(Admin(), query));

        Assert.Equal("Arama için en az 2 karakter girin.", ex.Message);
    }

    [Fact]
    public async Task Search_hides_soft_deleted_units_and_accounts()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        seed.A5.IsDeleted = true;
        seed.OwnerAccount.IsDeleted = true;
        await db.SaveChangesAsync();

        var results = await Tools(db).SearchAsync(Admin(), "sahin");

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_matches_phone_and_returns_accounts_for_hesaplar_viewer()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db, AppRoles.Personel, AppModules.Daireler, AppModules.Hesaplar);

        var results = await tools.SearchAsync(User(AppRoles.Personel), "0532 111 22 33");

        Assert.Contains(results, x => x.Type == "unit" && x.Id == seed.A5.Id);
        Assert.Contains(results, x => x.Type == "account" && x.Id == seed.OwnerAccount.Id
            && x.Link == $"https://localhost/Accounts/Detail/{seed.OwnerAccount.Id}");
    }

    // Yalnız Daireler yetkisi: telefon aranır (Daireler OR Hesaplar), ama hesap sonucu gelmez.
    [Fact]
    public async Task Search_with_daireler_only_matches_phone_but_returns_no_accounts()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db, AppRoles.Personel, AppModules.Daireler);

        var byPhone = await tools.SearchAsync(User(AppRoles.Personel), "05321112233");
        var byName = await tools.SearchAsync(User(AppRoles.Personel), "sahin");

        Assert.Equal(seed.A5.Id, Assert.Single(byPhone).Id);
        Assert.NotEmpty(byName);
        Assert.All(byName, x => Assert.Equal("unit", x.Type));
    }

    // Yalnız Tahsilatlar yetkisi: daire, ad ve bakiye var; telefon yok, telefonla arama sonuç vermez.
    [Fact]
    public async Task Tahsilatlar_only_user_gets_units_and_names_but_no_phone()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db, AppRoles.Personel, AppModules.Tahsilatlar);
        var user = User(AppRoles.Personel);

        Assert.Empty(await tools.SearchAsync(user, "05321112233"));
        var found = Assert.Single(await tools.SearchAsync(user, "sahin"));
        Assert.Equal(seed.A5.Id, found.Id);

        var detail = await tools.GetUnitAsync(user, seed.A5.Id);
        Assert.Contains(detail.People, x => x.Name == "Şahin Yılmaz");
        Assert.All(detail.People, x => Assert.Null(x.Phone));
        // Daire sayfasını açamaz: bağlantısı da verilmez.
        Assert.Null(found.Link);
        Assert.Null(detail.Link);
    }

    // Ekstre sayfası Daireler yetkisi ister; Tahsilatlar yetkisi yetmez.
    [Fact]
    public async Task Unit_statement_requires_daireler()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var collector = Tools(db, AppRoles.Personel, AppModules.Tahsilatlar);
        var viewer = Tools(db, AppRoles.SadeceGoruntuleme, AppModules.Daireler);

        var denied = await Assert.ThrowsAsync<McpException>(() => collector.UnitStatementAsync(User(AppRoles.Personel), seed.A5.Id));
        Assert.Contains("yetkiniz yok (Daireler)", denied.Message);
        Assert.Equal(300m, (await viewer.UnitStatementAsync(User(AppRoles.SadeceGoruntuleme), seed.A5.Id)).ClosingBalance);
    }

    [Fact]
    public async Task Daireler_viewer_gets_unit_links()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db, AppRoles.Personel, AppModules.Daireler);
        var user = User(AppRoles.Personel);
        var link = $"https://localhost/Units/Detail/{seed.A5.Id}";

        Assert.Equal(link, Assert.Single(await tools.SearchAsync(user, "sahin")).Link);
        Assert.Equal(link, (await tools.GetUnitAsync(user, seed.A5.Id)).Link);
    }

    [Fact]
    public async Task Daireler_viewer_sees_phones_in_get_unit()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db, AppRoles.Personel, AppModules.Daireler);

        var detail = await tools.GetUnitAsync(User(AppRoles.Personel), seed.A5.Id);

        Assert.Equal("0532 111 22 33", Assert.Single(detail.People, x => x.Type == "Malik").Phone);
        Assert.Equal("0555 000 11 22", Assert.Single(detail.People, x => x.Type == "Kiracı").Phone);
    }

    [Fact]
    public async Task Tools_reject_user_without_daireler_or_tahsilatlar()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db, AppRoles.Personel, AppModules.Raporlar);
        var user = User(AppRoles.Personel);

        await Assert.ThrowsAsync<McpException>(() => tools.SearchAsync(user, "sahin"));
        await Assert.ThrowsAsync<McpException>(() => tools.GetUnitAsync(user, seed.A5.Id));
        await Assert.ThrowsAsync<McpException>(() => tools.UnitStatementAsync(user, seed.A5.Id));
    }

    [Fact]
    public async Task Get_unit_returns_open_installments_and_link()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var old = AddInstallment(db, seed, "2025-2026", 1_000m, remaining: 400m, Utc(2025, 9, 1));
        var paid = AddInstallment(db, seed, "2025-2026", 500m, remaining: 0m, Utc(2025, 10, 1));
        var newer = AddInstallment(db, seed, "2026-2027", 2_000m, remaining: 2_000m, Utc(2026, 9, 1));
        await db.SaveChangesAsync();
        for (var i = 1; i <= 6; i++)
        {
            await new CollectionService(db).CreateAsync(new CollectionCreateViewModel
            {
                BillingGroupId = seed.GroupId,
                PreferredUnitId = seed.A5.Id,
                Date = Utc(2025, 11, i),
                Amount = 10m * i,
                PaymentChannel = PaymentChannel.Cash,
                AccountKey = FinancialAccountHelper.CashKey(seed.CashBoxId)
            });
        }

        var detail = await Tools(db).GetUnitAsync(Admin(), seed.A5.Id);

        Assert.EndsWith($"/Units/Detail/{seed.A5.Id}", detail.Link);
        Assert.Equal("A-5", detail.Display);
        Assert.Equal("A", detail.Block);
        Assert.Equal("Küçük Daireler", detail.BillingGroup);
        Assert.Equal([old.Id, newer.Id], detail.OpenInstallments.Select(x => x.Id));
        Assert.DoesNotContain(detail.OpenInstallments, x => x.Id == paid.Id);
        Assert.Equal(1_000m, detail.OpenInstallments[0].Amount);
        Assert.Equal(400m, detail.OpenInstallments[0].Remaining);
        Assert.Equal("2025-2026", detail.OpenInstallments[0].Period);
        // 210 TL'lik genel tahsilat önce 300 TL'lik devir borcunu kapatır: 90 kalır.
        Assert.Equal(90m, detail.DevirRemaining);
        // Bakiye: 300 devir + 3.500 tahakkuk - 210 tahsilat; pozitif = borç.
        Assert.Equal(3_590m, detail.Balance);
        Assert.Equal(5, detail.RecentCollections.Count);
        Assert.Equal(60m, detail.RecentCollections[0].Amount);
        Assert.Equal("2025-11-06", detail.RecentCollections[0].Date);
        Assert.Equal("Ana Kasa", detail.RecentCollections[0].Account);
    }

    // Banka adı diğer araçlardaki gibi şubesiyle gelir: aynı bankanın iki hesabı karışmasın.
    [Fact]
    public async Task Get_unit_recent_collections_show_the_bank_branch()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var bank = new BankAccount { Name = "İş Bankası", Branch = "Merkez" };
        var branchless = new BankAccount { Name = "Halkbank" };
        db.BankAccounts.AddRange(bank, branchless);
        await db.SaveChangesAsync();
        foreach (var (account, day) in new[] { (bank, 1), (branchless, 2) })
        {
            db.Collections.Add(new Collection
            {
                UnitId = seed.A5.Id, BillingGroupId = seed.GroupId, Date = Utc(2025, 11, day), Amount = 10m,
                PaymentChannel = PaymentChannel.Bank, BankAccountId = account.Id
            });
        }

        await db.SaveChangesAsync();

        var detail = await Tools(db).GetUnitAsync(Admin(), seed.A5.Id);

        Assert.Equal(["Halkbank", "İş Bankası - Merkez"], detail.RecentCollections.Select(x => x.Account));
    }

    // Devir alacağı taksitlerden düşülür: kalanlar tahsilat formu ve Aidatlar ekranıyla aynı.
    [Fact]
    public async Task Get_unit_open_installments_match_the_screens_for_carried_credit()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        seed.A15.OpeningBalance = 5_000m;
        seed.A15.OpeningBalanceDate = Utc(2025, 7, 1);
        var installment = AddInstallment(db, seed, "2026-2027", 12_000m, remaining: 12_000m, Utc(2026, 7, 1), seed.A15);
        await db.SaveChangesAsync();
        var tools = Tools(db);

        var before = await tools.GetUnitAsync(Admin(), seed.A15.Id);

        var open = Assert.Single(before.OpenInstallments);
        Assert.Equal(installment.Id, open.Id);
        Assert.Equal(12_000m, open.Amount);
        Assert.Equal(7_000m, open.Remaining);
        Assert.Equal(7_000m, before.Balance);
        Assert.Equal(open.Remaining, (await CollectionFormAsync(db, installment.Id)).AllocationPreviewDebt);
        Assert.Equal(open.Remaining, (await DuesScreenRowAsync(db, installment.Id)).RemainingAmount);

        await new CollectionService(db).CreateAsync(new CollectionCreateViewModel
        {
            BillingGroupId = seed.GroupId,
            PreferredUnitId = seed.A15.Id,
            DuesInstallmentId = installment.Id,
            Date = Utc(2026, 8, 1),
            Amount = 7_000m,
            PaymentChannel = PaymentChannel.Cash,
            AccountKey = FinancialAccountHelper.CashKey(seed.CashBoxId)
        });
        var after = await tools.GetUnitAsync(Admin(), seed.A15.Id);

        // Ham kalan 5.000 olsa da devir alacağı karşılar: ekranlar da borç göstermez.
        Assert.Empty(after.OpenInstallments);
        Assert.Equal(0m, after.Balance);
        Assert.DoesNotContain((await CollectionFormAsync(db)).DuesInstallmentOptions, x => x.Value == installment.Id.ToString());
        var paid = await DuesScreenRowAsync(db, installment.Id);
        Assert.True(paid.IsPaid);
        Assert.Equal(0m, paid.RemainingAmount);
    }

    // Alacak en eski tahakkuktan başlar; artan kısmı sonrakine geçer.
    [Fact]
    public async Task Get_unit_applies_carried_credit_to_the_oldest_installments_first()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        seed.A15.OpeningBalance = 1_500m;
        seed.A15.OpeningBalanceDate = Utc(2025, 7, 1);
        AddInstallment(db, seed, "2025-2026", 1_000m, remaining: 1_000m, Utc(2025, 9, 1), seed.A15);
        var newer = AddInstallment(db, seed, "2026-2027", 2_000m, remaining: 2_000m, Utc(2026, 9, 1), seed.A15);
        await db.SaveChangesAsync();

        var detail = await Tools(db).GetUnitAsync(Admin(), seed.A15.Id);

        var open = Assert.Single(detail.OpenInstallments);
        Assert.Equal(newer.Id, open.Id);
        Assert.Equal(1_500m, open.Remaining);
        Assert.Equal((await DuesScreenRowAsync(db, newer.Id)).RemainingAmount, open.Remaining);
    }

    [Fact]
    public async Task Get_unit_rejects_unknown_and_deleted_unit()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        seed.A15.IsDeleted = true;
        await db.SaveChangesAsync();
        var tools = Tools(db);

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.GetUnitAsync(Admin(), 99999));
        Assert.Equal("Daire bulunamadı.", ex.Message);
        await Assert.ThrowsAsync<McpException>(() => tools.GetUnitAsync(Admin(), seed.A15.Id));
        await Assert.ThrowsAsync<McpException>(() => tools.UnitStatementAsync(Admin(), 99999));
    }

    [Fact]
    public async Task Unit_statement_filters_by_date_range()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        AddInstallment(db, seed, "2025-2026", 1_000m, remaining: 1_000m, Utc(2025, 9, 1));
        AddInstallment(db, seed, "2025-2026", 2_000m, remaining: 2_000m, Utc(2025, 10, 1));
        AddInstallment(db, seed, "2025-2026", 4_000m, remaining: 4_000m, Utc(2025, 11, 1));
        await db.SaveChangesAsync();
        var tools = Tools(db);

        var all = await tools.UnitStatementAsync(Admin(), seed.A5.Id);
        var ranged = await tools.UnitStatementAsync(Admin(), seed.A5.Id, "2025-10-01", "2025-10-31");

        Assert.Equal("A-5", ranged.Unit);
        var entry = Assert.Single(ranged.Entries);
        Assert.Equal("2025-10-01", entry.Date);
        Assert.Equal(2_000m, entry.Amount);
        // Yürüyen bakiye tam ekstredeki değerdir: 300 devir + 1.000 + 2.000.
        Assert.Equal(3_300m, entry.RunningBalance);
        Assert.Equal(3_300m, ranged.ClosingBalance);
        Assert.Equal(4, all.Entries.Count);
        Assert.Equal(7_300m, all.ClosingBalance);

        var from = await tools.UnitStatementAsync(Admin(), seed.A5.Id, "2025-10-02");
        Assert.Equal(4_000m, Assert.Single(from.Entries).Amount);
        Assert.Equal(7_300m, from.ClosingBalance);

        await Assert.ThrowsAsync<McpException>(
            () => tools.UnitStatementAsync(Admin(), seed.A5.Id, "2025-12-01", "2025-01-01"));
    }

    [Fact]
    public async Task Overdue_dues_orders_by_remaining_desc_and_respects_limit()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        AddInstallment(db, seed, "2025-2026", 1_000m, remaining: 1_000m, Utc(2025, 9, 1), seed.A15);
        AddInstallment(db, seed, "2026-2027", 3_000m, remaining: 3_000m, Utc(2026, 9, 1), seed.A15);
        AddInstallment(db, seed, "2025-2026", 9_000m, remaining: 9_000m, Utc(2025, 9, 1), seed.A150);
        await db.SaveChangesAsync();
        var tools = Tools(db, AppRoles.Personel, AppModules.Raporlar);
        var user = User(AppRoles.Personel);

        var all = await tools.OverdueDuesAsync(user);

        // A-5 de (300 devir borcu) listelenir; borcu olmayan B-115 listelenmez.
        Assert.Equal([seed.A150.Id, seed.A15.Id, seed.A5.Id], all.Rows.Select(x => x.UnitId));
        Assert.Equal(3, all.Count);
        Assert.Equal(9_000m + 4_000m + 300m, all.Total);
        Assert.Equal("2025-2026", all.Rows[0].OldestPeriod);
        Assert.Equal("2025-2026", all.Rows[1].OldestPeriod);
        Assert.Equal(4_000m, all.Rows[1].Remaining);
        // Raporlar yetkisi daire sayfasını açmaz: bağlantı yok.
        Assert.All(all.Rows, x => Assert.Null(x.Link));

        var limited = await tools.OverdueDuesAsync(user, limit: 1);
        Assert.Equal(seed.A150.Id, Assert.Single(limited.Rows).UnitId);
        Assert.Equal(3, limited.Count);
        Assert.Equal(9_000m + 4_000m + 300m, limited.Total);

        // Dönem verilse de devir borcu kalana girer: o dönemde taksiti olmayan A-5 "Devir" diye gelir.
        var period = await tools.OverdueDuesAsync(user, period: "2026-2027");
        Assert.Equal([(seed.A15.Id, "2026-2027"), (seed.A5.Id, "Devir")], period.Rows.Select(x => (x.UnitId!.Value, x.OldestPeriod!)));
        Assert.Equal(300m, period.Rows[1].Remaining);

        await Assert.ThrowsAsync<McpException>(() => tools.OverdueDuesAsync(user, period: "2026"));
    }

    [Fact]
    public async Task Overdue_dues_links_units_for_daireler_viewer()
    {
        await using var db = CreateDb();
        var seed = await SeedAsync(db);
        var tools = Tools(db, AppRoles.Personel, AppModules.Raporlar, AppModules.Daireler);

        var row = Assert.Single((await tools.OverdueDuesAsync(User(AppRoles.Personel))).Rows);

        Assert.Equal($"https://localhost/Units/Detail/{seed.A5.Id}", row.Link);
    }

    [Fact]
    public async Task Overdue_dues_requires_raporlar()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var tools = Tools(db, AppRoles.Personel, AppModules.Daireler);

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.OverdueDuesAsync(User(AppRoles.Personel)));

        Assert.Contains(AppModules.Raporlar, ex.Message);
    }

    // ---- yardımcılar ----

    private sealed record Seed(
        Unit A5, Unit A15, Unit A150, Unit B115, Account OwnerAccount, int GroupId, int CashBoxId);

    private static async Task<Seed> SeedAsync(ApplicationDbContext db)
    {
        var site = new Site { Name = "Site" };
        var blockA = new Block { Site = site, Name = "A" };
        var blockB = new Block { Site = site, Name = "B" };
        var owner = new Account { Name = "Şahin Yılmaz", AccountType = AccountType.Owner, Phone = "0532 111 22 33" };
        var tenant = new Account { Name = "Kiracı Kemal", AccountType = AccountType.Tenant, Phone = "0555 000 11 22" };
        var a5 = new Unit
        {
            Block = blockA,
            UnitNo = "5",
            OwnerName = "Şahin Yılmaz",
            Phone = "0532 111 22 33",
            OpeningBalance = -300m,
            OpeningBalanceDate = Utc(2025, 7, 1)
        };
        var a15 = new Unit { Block = blockA, UnitNo = "15" };
        var a150 = new Unit { Block = blockA, UnitNo = "150" };
        var b115 = new Unit { Block = blockB, UnitNo = "115" };
        var duesType = new DuesType { Name = "Aidat", Amount = 1_000m, Active = true };
        var group = new BillingGroup
        {
            Name = "Küçük Daireler",
            DuesType = duesType,
            EffectiveStartPeriod = "2025-2026",
            Active = true
        };
        var cash = new CashBox { Name = "Ana Kasa" };
        db.AddRange(site, blockA, blockB, owner, tenant, a5, a15, a150, b115, duesType, group, cash);
        db.UnitAccounts.AddRange(
            new UnitAccount { Unit = a5, Account = owner, Role = UnitAccountRole.Owner },
            new UnitAccount { Unit = a5, Account = tenant, Role = UnitAccountRole.Tenant });
        foreach (var unit in new[] { a5, a15, a150, b115 })
        {
            db.BillingGroupUnits.Add(new BillingGroupUnit { BillingGroup = group, Unit = unit, StartPeriod = "2025-2026" });
        }

        await db.SaveChangesAsync();
        return new Seed(a5, a15, a150, b115, owner, group.Id, cash.Id);
    }

    private static DuesInstallment AddInstallment(
        ApplicationDbContext db, Seed seed, string period, decimal amount, decimal remaining, DateTime accrual, Unit? unit = null)
    {
        var installment = new DuesInstallment
        {
            UnitId = (unit ?? seed.A5).Id,
            BillingGroupId = seed.GroupId,
            Period = period,
            AccrualDate = accrual,
            DueDate = accrual.AddMonths(1),
            Amount = amount,
            RemainingAmount = remaining,
            Status = remaining == 0m ? InstallmentStatus.Paid : InstallmentStatus.Open
        };
        db.DuesInstallments.Add(installment);
        return installment;
    }

    // Tahsilat ekranı; taksit seçiliyse AllocationPreviewDebt onun kalanıdır.
    private static async Task<CollectionCreateViewModel> CollectionFormAsync(ApplicationDbContext db, int? installmentId = null)
    {
        var controller = new CollectionsController(db, new CollectionService(db), null!, new DuesLedgerRowService(db));
        return (CollectionCreateViewModel)((ViewResult)await controller.Create(installmentId)).Model!;
    }

    // Aidatlar ekranındaki satır.
    private static async Task<DuesListItemViewModel> DuesScreenRowAsync(ApplicationDbContext db, int installmentId)
        => Assert.Single(await new DuesLedgerRowService(db).GetInstallmentRowsAsync(), x => x.Id == installmentId);

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // role verilirse o role yalnız belirtilen modüller için görüntüleme yetkisi tohumlanır.
    private static UnitTools Tools(ApplicationDbContext db, string? role = null, params string[] viewModules)
    {
        if (role is not null)
        {
            db.RolePermissions.AddRange(viewModules.Select(module =>
                new RolePermission { RoleName = role, Module = module, CanView = true }));
            db.SaveChanges();
        }

        var ledger = new UnitLedgerService(db, new DuesLedgerRowService(db));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OAuth:Issuer"] = "https://localhost/" })
            .Build();
        return new UnitTools(
            db,
            new PermissionService(db, new MemoryCache(new MemoryCacheOptions())),
            ledger,
            new UnitStatementService(ledger),
            new ReportingService(db, ledger, new DuesLedgerRowService(db)),
            new McpLinks(config));
    }

    private static ClaimsPrincipal Admin() => User(AppRoles.SistemYonetici);

    private static ClaimsPrincipal User(string role)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "Test"));
}
