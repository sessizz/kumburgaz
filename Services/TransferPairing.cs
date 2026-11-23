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
        var legs = rows.Where(IsDurableTransferLeg).ToList();
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

    public static Task<LedgerTransaction?> FindPairAsync(ApplicationDbContext db, LedgerTransaction leg, CancellationToken ct = default)
        => FindPairAsync(db, leg, track: true, ct);

    internal static async Task<LedgerTransaction?> FindPairAsync(
        ApplicationDbContext db, LedgerTransaction leg, bool track, CancellationToken ct = default)
    {
        if (!IsLegacyTransferLeg(leg))
        {
            return null;
        }

        var rows = track ? db.LedgerTransactions.AsTracking() : db.LedgerTransactions.AsNoTracking();
        var candidates = rows
            .Include(x => x.IncomeExpenseCategory)
            .Where(x => x.Id != leg.Id);
        if (leg.TransferGroupId is Guid groupId)
        {
            var groupMembers = await candidates.Where(x => x.TransferGroupId == groupId).ToListAsync(ct);
            return FindOppositeLeg(leg, groupMembers.Where(IsLegacyTransferLeg));
        }

        var key = LegacyKey(leg);
        var legacyCandidates = await candidates
            .Where(x => x.TransferGroupId == null && x.Date == key.Date && x.Amount == key.Amount && x.Description == key.Description)
            .ToListAsync(ct);
        return FindOppositeLeg(leg, legacyCandidates.Where(IsLegacyTransferLeg));
    }

    internal static async Task PreserveLegacyPairsForReorderAsync(
        ApplicationDbContext db, IEnumerable<LedgerTransaction> movingLedger)
    {
        var legs = movingLedger.Where(x => x.TransferGroupId == null && IsLegacyTransferLeg(x)).ToList();
        if (legs.Count == 0)
        {
            return;
        }

        var dates = legs.Select(x => x.Date).Distinct().ToArray();
        var candidates = await db.LedgerTransactions.Include(x => x.IncomeExpenseCategory)
            .Where(x => x.TransferGroupId == null && dates.Contains(x.Date)).ToListAsync();
        var byKey = candidates.Where(IsLegacyTransferLeg).ToLookup(LegacyKey);
        var pairs = new List<(LedgerTransaction Leg, LedgerTransaction Pair)>();
        foreach (var leg in legs)
        {
            var matches = byKey[LegacyKey(leg)].ToArray();
            var pair = matches.Length == 2 ? matches.SingleOrDefault(x => x.Id != leg.Id) : null;
            if (pair is null || IsIncoming(leg) == IsIncoming(pair) || !BelongToDifferentAccounts(leg, pair))
            {
                throw new InvalidOperationException("Karşı hareketi kesin olarak belirlenemeyen eski transfer sıralanamaz.");
            }

            pairs.Add((leg, pair));
        }

        // Kategori ve yön alanları korunur. Yalnızca ortak kimlik iki hesaba yazılır.
        foreach (var (leg, pair) in pairs)
        {
            leg.TransferGroupId = pair.TransferGroupId = Guid.NewGuid();
        }
    }

    private static bool BelongToDifferentAccounts(LedgerTransaction leg, LedgerTransaction pair) =>
        leg.CashBoxId.HasValue != leg.BankAccountId.HasValue
        && pair.CashBoxId.HasValue != pair.BankAccountId.HasValue
        && (leg.CashBoxId != pair.CashBoxId || leg.BankAccountId != pair.BankAccountId);

    internal static async Task<Dictionary<int, LedgerTransaction>> FindPairsAsync(
        ApplicationDbContext db, IEnumerable<LedgerTransaction> legs)
    {
        var transfers = legs.Where(IsLegacyTransferLeg).ToList();
        var pairs = new Dictionary<int, LedgerTransaction>();
        if (transfers.Count == 0)
        {
            return pairs;
        }

        var groupIds = transfers.Where(x => x.TransferGroupId.HasValue).Select(x => x.TransferGroupId).Distinct().ToArray();
        var legacyDates = transfers.Where(x => x.TransferGroupId == null).Select(x => x.Date).Distinct().ToArray();
        var candidates = await db.LedgerTransactions
            .AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Where(x => groupIds.Contains(x.TransferGroupId) || (x.TransferGroupId == null && legacyDates.Contains(x.Date)))
            .ToListAsync();
        var groupedCandidates = candidates
            .Where(x => x.TransferGroupId.HasValue && IsLegacyTransferLeg(x))
            .ToLookup(x => x.TransferGroupId);
        var legacyCandidates = candidates
            .Where(x => x.TransferGroupId == null && IsLegacyTransferLeg(x))
            .ToLookup(LegacyKey);

        foreach (var leg in transfers)
        {
            var matches = leg.TransferGroupId.HasValue
                ? groupedCandidates[leg.TransferGroupId]
                : legacyCandidates[LegacyKey(leg)];
            var pair = FindOppositeLeg(leg, matches);
            if (pair is not null)
            {
                pairs.Add(leg.Id, pair);
            }
        }

        return pairs;
    }

    // Açılışta yalnız işaretli veya transfer kategorisindeki hareketler eşlenir.
    private static bool IsDurableTransferLeg(LedgerTransaction leg) =>
        leg.IsTransfer || leg.IncomeExpenseCategory?.Name.Contains("Transfer", StringComparison.OrdinalIgnoreCase) == true;

    // Eski açıklamalar, açılışta eşlenmese de karşı hareket aramasına dahildir.
    internal static bool IsLegacyTransferLeg(LedgerTransaction leg) =>
        IsTransferForDisplay(leg)
        || string.Equals(leg.Description, "Para transferi", StringComparison.OrdinalIgnoreCase)
        || string.Equals(leg.Description, "Bankaya yatır", StringComparison.OrdinalIgnoreCase);

    // Kasa/banka detayında satırın türünü ve tutar işaretini belirler.
    internal static bool IsTransferForDisplay(LedgerTransaction leg) =>
        IsDurableTransferLeg(leg)
        || leg.Description?.StartsWith("Para transferi:", StringComparison.OrdinalIgnoreCase) == true
        || leg.Description?.StartsWith("Bankaya yatır:", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsIncoming(LedgerTransaction leg) =>
        leg.IsTransfer ? leg.TransferIsIncoming : leg.IncomeExpenseCategory?.Type == CategoryTypeHelper.Gelir;

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

            var pair = FindOppositeLeg(leg, byKey[LegacyKey(leg)].Where(x => !pairedIds.Contains(x.Id)));
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

    private static LedgerTransaction? FindOppositeLeg(LedgerTransaction leg, IEnumerable<LedgerTransaction> candidates)
    {
        var incoming = IsIncoming(leg);
        return candidates
            .Where(x => x.Id != leg.Id && IsIncoming(x) != incoming)
            .MinBy(x => (leg.TransferGroupId.HasValue ? x.Id : Math.Abs((long)x.Id - leg.Id), x.Id));
    }
}
