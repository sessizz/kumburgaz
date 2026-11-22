using Kumburgaz.Web.Data;
using Kumburgaz.Web.Mcp;
using Kumburgaz.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using Xunit;

namespace Kumburgaz.Web.Tests;

// Tekrar koruması gerçek işlem (transaction) ister: testler dosya tabanlı SQLite üzerinde koşar.
public sealed class McpIdempotencyStoreTests : IDisposable
{
    private const string UserId = "user-1";
    private const string Tool = "create_expense";
    private const string Key = "key-12345";
    private const string Fingerprint = "fp-1";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"kumburgaz-idem-{Guid.NewGuid():N}.db");

    public McpIdempotencyStoreTests()
    {
        using var db = OpenDb();
        db.Database.EnsureCreated();
    }

    [Fact]
    public async Task TryGet_ignores_rows_older_than_24_hours()
    {
        await AddRowAsync("key-fresh", TimeSpan.FromHours(23.9));
        await AddRowAsync("key-stale", TimeSpan.FromHours(24.1));
        await using var db = OpenDb();
        var store = new McpIdempotencyStore(db);

        Assert.Equal("{}", await store.TryGetAsync(UserId, Tool, "key-fresh"));
        Assert.Null(await store.TryGetAsync(UserId, Tool, "key-stale"));
        Assert.Null(await store.TryGetAsync(UserId, Tool, "key-missing"));
        Assert.Null(await store.TryGetAsync("user-2", Tool, "key-fresh"));
        Assert.Null(await store.TryGetAsync(UserId, "create_income", "key-fresh"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Save_stores_row_and_purges_rows_older_than_24_hours(bool sqlite)
    {
        await using var db = sqlite ? OpenDb() : InMemoryDb();
        db.McpIdempotencyKeys.AddRange(Row("key-fresh", TimeSpan.FromHours(23.9)), Row("key-stale", TimeSpan.FromHours(24.1)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new McpIdempotencyStore(db).SaveAsync(UserId, Tool, Key, """{"a":1}""");

        var keys = await db.McpIdempotencyKeys.AsNoTracking().OrderBy(x => x.Key).Select(x => x.Key).ToListAsync();
        Assert.Equal([Key, "key-fresh"], keys);
        Assert.Equal("""{"a":1}""", await new McpIdempotencyStore(db).TryGetAsync(UserId, Tool, Key));
    }

    // Aynı anahtarla ikinci çalıştırma yazmaz, ilk sonucu döner (ağ yeniden denemesi).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Replay_returns_first_result_for_same_inputs(bool sqlite)
    {
        await using var db = sqlite ? OpenDb() : InMemoryDb();
        var store = new McpIdempotencyStore(db);

        Assert.Null(await store.TryReplayAsync<Result>(UserId, Tool, Key, Fingerprint));
        var first = await store.RunOnceAsync(UserId, Tool, Key, Fingerprint, () => Task.FromResult(new Result(7)));
        var replay = await store.TryReplayAsync<Result>(UserId, Tool, Key, Fingerprint);

        Assert.Equal(new Result(7), first);
        Assert.Equal(new Result(7), replay);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Key_reuse_with_different_inputs_is_rejected(bool sqlite)
    {
        await using var db = sqlite ? OpenDb() : InMemoryDb();
        var store = new McpIdempotencyStore(db);
        await store.RunOnceAsync(UserId, Tool, Key, Fingerprint, () => Task.FromResult(new Result(7)));

        var ex = await Assert.ThrowsAsync<McpException>(() => store.TryReplayAsync<Result>(UserId, Tool, Key, "fp-2"));

        Assert.Equal("Bu idempotency_key farklı bir işlem için kullanılmış; yeni işlem için yeni bir anahtar gönderin.", ex.Message);
    }

    // Kayıt yokken başlayan iki çağrıdan geç kalanı anahtar çakışmasına düşer: yazmaz, kazananın sonucunu döner.
    [Fact]
    public async Task Conflicting_run_returns_winners_result_without_writing()
    {
        await using var first = OpenDb();
        await using var second = OpenDb();
        await RunAsync(first, () => AddCashBoxAsync(first, "Kazanan"));
        var ran = false;

        var result = await RunAsync(second, () =>
        {
            ran = true;
            return AddCashBoxAsync(second, "Kaybeden");
        });

        Assert.False(ran);
        Assert.Equal(await SingleCashBoxIdAsync("Kazanan"), result.Id);
        Assert.Equal(0, await CountCashBoxesAsync("Kaybeden"));
    }

    [Fact]
    public async Task Conflicting_run_with_different_inputs_is_rejected_without_writing()
    {
        await using var first = OpenDb();
        await using var second = OpenDb();
        await RunAsync(first, () => AddCashBoxAsync(first, "Kazanan"));

        await Assert.ThrowsAsync<McpException>(() => new McpIdempotencyStore(second).RunOnceAsync(
            UserId, Tool, Key, "fp-2", () => AddCashBoxAsync(second, "Kaybeden")));

        Assert.Equal(0, await CountCashBoxesAsync("Kaybeden"));
    }

    // İlk çağrı henüz yazarken gelen ikinci çağrı bekler, sonra ilkin sonucunu döner.
    [Fact]
    public async Task Overlapping_run_waits_and_returns_winners_result()
    {
        await using var first = OpenDb();
        Task<Result>? overlapping = null;
        var overlappingRan = false;

        var winner = await RunAsync(first, async () =>
        {
            overlapping = StartOverlapping(() => overlappingRan = true);
            await Task.Delay(300);
            Assert.False(overlapping.IsCompleted);
            return await AddCashBoxAsync(first, "Kazanan");
        });
        var loser = await overlapping!;

        Assert.False(overlappingRan);
        Assert.Equal(winner, loser);
        Assert.Equal(1, await CountCashBoxesAsync("Kazanan"));
        Assert.Equal(0, await CountCashBoxesAsync("Eşzamanlı"));
    }

    // İlk çağrı başarısız olursa bekleyen çağrı anahtarı alıp kendisi yazar.
    [Fact]
    public async Task Overlapping_run_writes_when_first_one_fails()
    {
        await using var first = OpenDb();
        Task<Result>? overlapping = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(first, async () =>
        {
            overlapping = StartOverlapping(() => { });
            await AddCashBoxAsync(first, "Başarısız");
            await Task.Delay(300);
            throw new InvalidOperationException("yazılamadı");
        }));
        var result = await overlapping!;

        Assert.Equal(0, await CountCashBoxesAsync("Başarısız"));
        Assert.Equal(await SingleCashBoxIdAsync("Eşzamanlı"), result.Id);
    }

    [Fact]
    public async Task Failed_write_leaves_nothing_and_key_reusable_on_same_context()
    {
        await using var db = OpenDb();

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(db, async () =>
        {
            await AddCashBoxAsync(db, "Yarım");
            throw new InvalidOperationException("yazılamadı");
        }));

        Assert.Equal(0, await CountCashBoxesAsync("Yarım"));
        Assert.Equal(0, await db.McpIdempotencyKeys.CountAsync());
        Assert.Equal(0, await db.AuditLogs.CountAsync());
        Assert.Null(await new McpIdempotencyStore(db).TryReplayAsync<Result>(UserId, Tool, Key, Fingerprint));

        var retried = await RunAsync(db, () => AddCashBoxAsync(db, "Tam"));
        Assert.Equal(await SingleCashBoxIdAsync("Tam"), retried.Id);
        Assert.Equal(0, await CountCashBoxesAsync("Yarım"));
    }

    // Başarısız SaveChanges'ın izlemede bıraktığı satır sonraki kayda karışmamalı.
    [Fact]
    public async Task Failed_save_changes_does_not_leak_tracked_rows_into_the_retry()
    {
        await using var db = OpenDb();

        await Assert.ThrowsAsync<DbUpdateException>(() => RunAsync(db, async () =>
        {
            // Olmayan kategori: yabancı anahtar hatası.
            db.LedgerTransactions.Add(new LedgerTransaction { Date = DateTime.UtcNow, Amount = 5m, IncomeExpenseCategoryId = 99_999 });
            await db.SaveChangesAsync();
            return new Result(0);
        }));

        Assert.Empty(db.ChangeTracker.Entries());
        await RunAsync(db, () => AddCashBoxAsync(db, "Tam"));
        Assert.Equal(0, await db.LedgerTransactions.CountAsync());
        Assert.Equal(1, await CountCashBoxesAsync("Tam"));
    }

    // Testlerin InMemory sağlayıcısında işlem yok: anahtar satırı elle geri alınır.
    [Fact]
    public async Task Failed_write_leaves_key_reusable_on_in_memory_provider()
    {
        await using var db = InMemoryDb();

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(db, () => throw new InvalidOperationException("yazılamadı")));

        Assert.Equal(0, await db.McpIdempotencyKeys.CountAsync());
        Assert.Equal(new Result(3), await RunAsync(db, () => Task.FromResult(new Result(3))));
    }

    // 24 saati geçmiş anahtar yok sayılır: aynı anahtarla yeni işlem yazılır.
    [Fact]
    public async Task Expired_key_can_be_used_again()
    {
        await AddRowAsync(Key, TimeSpan.FromHours(25));
        await using var db = OpenDb();

        var result = await RunAsync(db, () => AddCashBoxAsync(db, "Yeni"));

        Assert.Equal(await SingleCashBoxIdAsync("Yeni"), result.Id);
        Assert.Equal(result, await new McpIdempotencyStore(db).TryReplayAsync<Result>(UserId, Tool, Key, Fingerprint));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("          ")]
    public void RequireKey_rejects_missing_key(string? key)
    {
        var ex = Assert.Throws<McpException>(() => McpIdempotencyStore.RequireKey(key));
        Assert.Equal("confirm: true için idempotency_key gönderin.", ex.Message);
    }

    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void RequireKey_accepts_8_to_100_characters(int length, bool accepted)
    {
        var key = new string('k', length);
        if (accepted)
        {
            Assert.Equal(key, McpIdempotencyStore.RequireKey(key));
            return;
        }

        var ex = Assert.Throws<McpException>(() => McpIdempotencyStore.RequireKey(key));
        Assert.Equal("idempotency_key 8-100 karakter olmalı.", ex.Message);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    private sealed record Result(int Id);

    private static Task<Result> RunAsync(ApplicationDbContext db, Func<Task<Result>> write)
        => new McpIdempotencyStore(db).RunOnceAsync(UserId, Tool, Key, Fingerprint, write);

    // Ayrı bağlamda (ayrı istek gibi) aynı anahtarla çalışır; SQLite beklemesi iş parçacığını tutar.
    private Task<Result> StartOverlapping(Action onWrite) => Task.Run(async () =>
    {
        await using var db = OpenDb();
        return await RunAsync(db, () =>
        {
            onWrite();
            return AddCashBoxAsync(db, "Eşzamanlı");
        });
    });

    private static async Task<Result> AddCashBoxAsync(ApplicationDbContext db, string name)
    {
        var cashBox = new CashBox { Name = name, OpeningBalanceDate = DateTime.UtcNow };
        db.CashBoxes.Add(cashBox);
        await db.SaveChangesAsync();
        return new Result(cashBox.Id);
    }

    private async Task<int> SingleCashBoxIdAsync(string name)
    {
        await using var db = OpenDb();
        return await db.CashBoxes.Where(x => x.Name == name).Select(x => x.Id).SingleAsync();
    }

    private async Task<int> CountCashBoxesAsync(string name)
    {
        await using var db = OpenDb();
        return await db.CashBoxes.CountAsync(x => x.Name == name);
    }

    private async Task AddRowAsync(string key, TimeSpan age)
    {
        await using var db = OpenDb();
        db.McpIdempotencyKeys.Add(Row(key, age));
        await db.SaveChangesAsync();
    }

    private static McpIdempotencyKey Row(string key, TimeSpan age) => new()
    {
        UserId = UserId, Tool = Tool, Key = key, ResultJson = "{}", CreatedAt = DateTime.UtcNow - age
    };

    private ApplicationDbContext OpenDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private static ApplicationDbContext InMemoryDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
