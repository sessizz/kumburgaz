using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kumburgaz.Web.Tests.Integration;

[Collection(IntegrationCollection.Name)]
public class OpenReaderGuardTests(KumburgazAppFactory factory)
{
    // Kesici bütün entegrasyon testlerinin kullandığı bağlamda etkin olmalı.
    [Fact]
    public void Guard_is_active_in_the_application_context()
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();

        Assert.Contains(factory.OpenReaderGuard, options.FindExtension<CoreOptionsExtension>()!.Interceptors ?? []);
    }

    [Fact]
    public async Task Command_during_open_reader_fails_and_is_recorded()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var guard = new OpenReaderGuard();
        await using var db = await CreateDbAsync(connection, guard);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var cashBox in db.CashBoxes.AsAsyncEnumerable())
            {
                await db.CashBoxes.CountAsync(x => x.Id == cashBox.Id);
            }
        });
        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var cashBox in db.CashBoxes.AsEnumerable())
            {
                db.CashBoxes.Where(x => x.Id == cashBox.Id).ExecuteDelete();
            }
        });

        Assert.Equal(2, guard.Violations.Count);
    }

    [Fact]
    public async Task Commands_after_the_reader_is_closed_are_allowed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var guard = new OpenReaderGuard();
        await using var db = await CreateDbAsync(connection, guard);

        foreach (var cashBox in await db.CashBoxes.ToListAsync())
        {
            await db.CashBoxes.CountAsync(x => x.Id == cashBox.Id);
        }

        await foreach (var _ in db.CashBoxes.AsAsyncEnumerable())
        {
            break;
        }

        db.CashBoxes.Add(NewCashBox());
        await db.SaveChangesAsync();
        Assert.Empty(guard.Violations);
    }

    private static async Task<ApplicationDbContext> CreateDbAsync(SqliteConnection connection, OpenReaderGuard guard)
    {
        await connection.OpenAsync();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(guard)
            .Options);
        await db.Database.EnsureCreatedAsync();
        db.CashBoxes.AddRange(NewCashBox(), NewCashBox());
        await db.SaveChangesAsync();
        return db;
    }

    private static CashBox NewCashBox() => new() { Name = Guid.NewGuid().ToString("N"), OpeningBalanceDate = DateTime.UtcNow };
}
