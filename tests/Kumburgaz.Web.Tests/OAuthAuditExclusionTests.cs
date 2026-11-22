using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class OAuthAuditExclusionTests
{
    // OpenIddict satırları (istemci sırrı, token içeriği) ve MCP tekrar koruması sonuçları
    // denetim kaydına kopyalanmaz; normal varlıklar denetlenmeye devam eder.
    [Fact]
    public async Task OpenIddict_and_mcp_rows_are_not_audited()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .UseOpenIddict()
            .Options;
        await using var db = new ApplicationDbContext(options);

        var application = new OpenIddictEntityFrameworkCoreApplication { ClientId = "claude", ClientSecret = "hash" };
        db.Add(application);
        db.Add(new OpenIddictEntityFrameworkCoreToken { Application = application, Payload = "token" });
        db.McpIdempotencyKeys.Add(new McpIdempotencyKey
        {
            UserId = "u1",
            Tool = "create_expense",
            Key = "key-12345",
            ResultJson = "{}",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.AuditLogs.CountAsync());

        db.RolePermissions.Add(new RolePermission { RoleName = "Personel", Module = "Panel", CanView = true });
        await db.SaveChangesAsync();

        var log = Assert.Single(await db.AuditLogs.ToListAsync());
        Assert.Equal(nameof(RolePermission), log.EntityName);
    }
}
