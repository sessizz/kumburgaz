using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class TransferPairingTests
{
    private static readonly DateTime TransferDate = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Backfill_pairs_legs_that_match_today_rule()
    {
        await using var db = CreateDb();
        var outgoing = CreateLeg(1, incoming: false);
        var incoming = CreateLeg(2, incoming: true);
        db.LedgerTransactions.AddRange(outgoing, incoming);
        await db.SaveChangesAsync();

        var count = await TransferPairing.BackfillAsync(db, NullLogger.Instance);

        Assert.Equal(1, count);
        db.ChangeTracker.Clear();
        var legs = await db.LedgerTransactions.OrderBy(x => x.Id).ToListAsync();
        Assert.NotNull(legs[0].TransferGroupId);
        Assert.Equal(legs[0].TransferGroupId, legs[1].TransferGroupId);
    }

    [Fact]
    public async Task Backfill_pairs_two_identical_transfers_into_two_groups()
    {
        await using var db = CreateDb();
        var firstOutgoing = CreateLeg(1, incoming: false);
        var firstIncoming = CreateLeg(2, incoming: true);
        var secondOutgoing = CreateLeg(3, incoming: false);
        var secondIncoming = CreateLeg(4, incoming: true);
        db.LedgerTransactions.AddRange(secondOutgoing, firstIncoming, firstOutgoing, secondIncoming);
        await db.SaveChangesAsync();

        var count = await TransferPairing.BackfillAsync(db, NullLogger.Instance);

        Assert.Equal(2, count);
        Assert.NotNull(firstOutgoing.TransferGroupId);
        Assert.Equal(firstOutgoing.TransferGroupId, firstIncoming.TransferGroupId);
        Assert.NotNull(secondOutgoing.TransferGroupId);
        Assert.Equal(secondOutgoing.TransferGroupId, secondIncoming.TransferGroupId);
        Assert.NotEqual(firstOutgoing.TransferGroupId, secondOutgoing.TransferGroupId);
    }

    [Fact]
    public async Task Backfill_is_idempotent()
    {
        await using var db = CreateDb();
        var outgoing = CreateLeg(1, incoming: false);
        var incoming = CreateLeg(2, incoming: true);
        db.LedgerTransactions.AddRange(outgoing, incoming);
        await db.SaveChangesAsync();
        await TransferPairing.BackfillAsync(db, NullLogger.Instance);
        var groupId = outgoing.TransferGroupId;
        Assert.NotNull(groupId);
        db.ChangeTracker.Clear();

        var count = await TransferPairing.BackfillAsync(db, NullLogger.Instance);

        Assert.Equal(0, count);
        var legs = await db.LedgerTransactions.ToListAsync();
        Assert.All(legs, leg => Assert.Equal(groupId, leg.TransferGroupId));
    }

    [Fact]
    public async Task Backfill_leaves_unmatched_leg_null()
    {
        await using var db = CreateDb();
        var outgoing = CreateLeg(1, incoming: false);
        var groupedIncoming = CreateLeg(2, incoming: true);
        var existingGroupId = Guid.NewGuid();
        groupedIncoming.TransferGroupId = existingGroupId;
        var differentDate = CreateLeg(3, incoming: true);
        differentDate.Date = TransferDate.AddTicks(10);
        var differentAmount = CreateLeg(4, incoming: true);
        differentAmount.Amount = outgoing.Amount + 1m;
        var differentDescription = CreateLeg(5, incoming: true);
        differentDescription.Description = "Para transferi: Başka işlem";
        db.LedgerTransactions.AddRange(outgoing, groupedIncoming, differentDate, differentAmount, differentDescription);
        await db.SaveChangesAsync();

        var count = await TransferPairing.BackfillAsync(db, NullLogger.Instance);

        Assert.Equal(0, count);
        Assert.Null(outgoing.TransferGroupId);
        Assert.Equal(existingGroupId, groupedIncoming.TransferGroupId);
    }

    [Fact]
    public async Task FindPair_uses_group_id_even_when_dates_differ()
    {
        await using var db = CreateDb();
        var outgoing = CreateLeg(1, incoming: false);
        var legacyMatch = CreateLeg(2, incoming: true);
        var incoming = CreateLeg(3, incoming: true);
        outgoing.TransferGroupId = incoming.TransferGroupId = Guid.NewGuid();
        incoming.Date = TransferDate.AddTicks(10);
        db.LedgerTransactions.AddRange(outgoing, legacyMatch, incoming);
        await db.SaveChangesAsync();

        var pair = await TransferPairing.FindPairAsync(db, outgoing);

        Assert.Equal(incoming.Id, pair?.Id);
    }

    [Fact]
    public async Task FindPair_falls_back_to_legacy_rule_without_group_id()
    {
        await using var db = CreateDb();
        var source = CreateLeg(10, incoming: false);
        var groupedCandidate = CreateLeg(11, incoming: true);
        groupedCandidate.TransferGroupId = Guid.NewGuid();
        var sameDirection = CreateLeg(12, incoming: false);
        var nearest = CreateLeg(13, incoming: true);
        var farther = CreateLeg(14, incoming: true);
        db.LedgerTransactions.AddRange(farther, source, nearest, sameDirection, groupedCandidate);
        await db.SaveChangesAsync();

        var pair = await TransferPairing.FindPairAsync(db, source);

        Assert.Equal(nearest.Id, pair?.Id);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static LedgerTransaction CreateLeg(int id, bool incoming) => new()
    {
        Id = id,
        Date = TransferDate,
        Amount = 100m,
        Description = "Para transferi",
        IsTransfer = true,
        TransferIsIncoming = incoming
    };
}
