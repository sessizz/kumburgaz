using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public static class TransferPairing
{
    public static async Task<int> BackfillAsync(ApplicationDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var rows = await db.LedgerTransactions
            .Include(x => x.IncomeExpenseCategory)
            .Where(x => x.TransferGroupId == null)
            .ToListAsync(ct);
        var legs = rows.Where(IsTransferLedger).ToList();
        var pairs = MatchLegacyLegs(legs);
        foreach (var (leg, pair) in pairs)
        {
            leg.TransferGroupId = pair.TransferGroupId = Guid.NewGuid();
        }

        if (pairs.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("{TransferPairCount} eski transfer eşlendi.", pairs.Count);
        }

        var unmatchedIds = legs.Where(x => x.TransferGroupId == null).Select(x => x.Id).Order();
        if (legs.Count > pairs.Count * 2)
        {
            logger.LogWarning("Karşı hareketi bulunamayan eski transferler: {TransferIds}.", string.Join(", ", unmatchedIds));
        }

        return pairs.Count;
    }

    public static async Task<LedgerTransaction?> FindPairAsync(ApplicationDbContext db, LedgerTransaction leg, CancellationToken ct = default)
    {
        var candidates = db.LedgerTransactions.Where(x => x.Id != leg.Id);
        if (leg.TransferGroupId is Guid groupId)
        {
            return await candidates
                .Where(x => x.TransferGroupId == groupId)
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync(ct);
        }

        var key = LegacyKey(leg);
        var legacyCandidates = await candidates
            .Include(x => x.IncomeExpenseCategory)
            .Where(x => x.TransferGroupId == null && x.Date == key.Date && x.Amount == key.Amount && x.Description == key.Description)
            .ToListAsync(ct);
        return FindLegacyPair(leg, legacyCandidates.Where(IsTransferLedger));
    }

    internal static bool IsTransferLedger(LedgerTransaction tx)
    {
        var category = tx.IncomeExpenseCategory?.Name ?? string.Empty;
        var description = tx.Description ?? string.Empty;
        return tx.IsTransfer
            || category.Contains("Transfer", StringComparison.OrdinalIgnoreCase)
            || description.StartsWith("Para transferi:", StringComparison.OrdinalIgnoreCase)
            || description.StartsWith("Bankaya yatır:", StringComparison.OrdinalIgnoreCase)
            || description.Equals("Para transferi", StringComparison.OrdinalIgnoreCase)
            || description.Equals("Bankaya yatır", StringComparison.OrdinalIgnoreCase);
    }

    internal static IEnumerable<LedgerTransaction> FindUnmatchedGroups(IReadOnlyList<LedgerTransaction> transfers)
    {
        foreach (var group in transfers.Where(x => x.TransferGroupId.HasValue).GroupBy(x => x.TransferGroupId))
        {
            if (group.Count() != 2 || group.Count(IsIncoming) != 1)
            {
                yield return group.MinBy(x => x.Id)!;
            }
        }

        var legacyLegs = transfers.Where(x => x.TransferGroupId == null).ToList();
        var pairedIds = MatchLegacyLegs(legacyLegs)
            .SelectMany(pair => new[] { pair.Leg.Id, pair.Pair.Id })
            .ToHashSet();
        foreach (var group in legacyLegs.GroupBy(LegacyKey))
        {
            if (group.Any(x => !pairedIds.Contains(x.Id)))
            {
                yield return group.MinBy(x => x.Id)!;
            }
        }
    }

    private static List<(LedgerTransaction Leg, LedgerTransaction Pair)> MatchLegacyLegs(List<LedgerTransaction> legs)
    {
        var byKey = legs.ToLookup(LegacyKey);
        var pairedIds = new HashSet<int>();
        var pairs = new List<(LedgerTransaction, LedgerTransaction)>();
        foreach (var leg in legs.OrderBy(x => x.Id))
        {
            if (pairedIds.Contains(leg.Id))
            {
                continue;
            }

            var pair = FindLegacyPair(leg, byKey[LegacyKey(leg)].Where(x => !pairedIds.Contains(x.Id)));
            if (pair is null)
            {
                continue;
            }

            pairedIds.Add(leg.Id);
            pairedIds.Add(pair.Id);
            pairs.Add((leg, pair));
        }

        return pairs;
    }

    private static (DateTime Date, decimal Amount, string? Description) LegacyKey(LedgerTransaction leg) =>
        (leg.Date, leg.Amount, leg.Description);

    private static bool IsIncoming(LedgerTransaction leg) =>
        leg.IsTransfer ? leg.TransferIsIncoming : leg.IncomeExpenseCategory?.Type == CategoryTypeHelper.Gelir;

    private static LedgerTransaction? FindLegacyPair(LedgerTransaction leg, IEnumerable<LedgerTransaction> candidates)
    {
        var incoming = IsIncoming(leg);
        return candidates
            .Where(x => x.Id != leg.Id && x.TransferIsIncoming != incoming)
            .OrderBy(x => Math.Abs((long)x.Id - leg.Id))
            .ThenBy(x => x.Id)
            .FirstOrDefault();
    }
}
