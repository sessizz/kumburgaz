using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

/// <summary>
/// Bir daireye ait tüm finansal hareketleri (devir bakiyesi, aidat tahakkukları,
/// tahsilatlar) tarihe göre sıralayarak yürüyen bakiyeli ekstre üretir.
/// </summary>
public class UnitStatementService(ApplicationDbContext db)
{
    public async Task<List<StatementEntry>> BuildAsync(int unitId)
    {
        var unit = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == unitId);
        if (unit is null) return [];

        var entries = new List<StatementEntry>();

        // 1) Devir bakiyesi
        if (unit.OpeningBalance != 0m && unit.OpeningBalanceDate.HasValue)
        {
            var amount = -unit.OpeningBalance; // pozitif bakiye = alacak → negatif borç
            entries.Add(new StatementEntry
            {
                Kind = StatementEntryKind.OpeningBalance,
                Date = unit.OpeningBalanceDate.Value,
                Description = unit.OpeningBalance > 0 ? "Devir Bakiyesi (Alacak)" : "Devir Bakiyesi (Devreden Borç)",
                Amount = amount
            });
        }

        // 2) Aidat tahakkukları (borç)
        var installments = await db.DuesInstallments.AsNoTracking()
            .Include(x => x.BillingGroup).ThenInclude(x => x!.DuesType)
            .Include(x => x.BillingGroup).ThenInclude(x => x!.Units)
            .Where(x => x.UnitId == unitId
                || (x.UnitId == null && x.BillingGroup!.Units.Any(u => u.UnitId == unitId)))
            .ToListAsync();

        foreach (var inst in installments)
        {
            entries.Add(new StatementEntry
            {
                Kind = StatementEntryKind.Debt,
                Date = inst.AccrualDate,
                Description = $"{inst.Period} - {inst.BillingGroup?.DuesType?.Name ?? "Aidat"}",
                Amount = inst.Amount
            });
        }

        // 3) Tahsilatlar — bu dairenin taksitlerine yapılan tahsisler
        var installmentIds = installments.Select(x => x.Id).ToList();
        if (installmentIds.Count > 0)
        {
            var allocations = await db.CollectionAllocations.AsNoTracking()
                .Include(x => x.Collection)
                .Include(x => x.DuesInstallment)
                .ThenInclude(x => x!.BillingGroup)
                .ThenInclude(x => x!.DuesType)
                .Where(x => installmentIds.Contains(x.DuesInstallmentId))
                .ToListAsync();

            foreach (var alloc in allocations.Where(a => a.Collection is not null))
            {
                var period = alloc.DuesInstallment?.Period ?? "";
                var typeName = alloc.DuesInstallment?.BillingGroup?.DuesType?.Name ?? "Aidat";
                entries.Add(new StatementEntry
                {
                    Kind = StatementEntryKind.Collection,
                    Date = alloc.Collection!.Date,
                    Description = $"{period} - {typeName} Tahsilatı",
                    Amount = -alloc.AppliedAmount
                });
            }
        }

        var unitCollections = await db.Collections.AsNoTracking()
            .Include(x => x.Allocations)
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.DuesType)
            .Where(x => x.UnitId == unitId)
            .ToListAsync();

        foreach (var collection in unitCollections)
        {
            var advanceAmount = collection.Amount - collection.Allocations.Sum(x => x.AppliedAmount);
            if (advanceAmount <= 0)
            {
                continue;
            }

            entries.Add(new StatementEntry
            {
                Kind = StatementEntryKind.Collection,
                Date = collection.Date,
                Description = $"{collection.BillingGroup?.DuesType?.Name ?? "Aidat"} Avans Tahsilatı",
                Amount = -advanceAmount
            });
        }

        // Tarihe göre sırala (en eski önce), borcu aşan tahsilatları avans olarak ayır ve yürüyen bakiyeyi hesapla
        var orderedRaw = entries
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Kind == StatementEntryKind.Debt ? 0 : 1)
            .ToList();

        var ordered = new List<StatementEntry>();
        decimal running = 0m;
        foreach (var entry in orderedRaw)
        {
            if (entry.Kind == StatementEntryKind.Collection && entry.Amount < 0)
            {
                var collectionAmount = -entry.Amount;
                if (running <= 0)
                {
                    AddStatementEntry(ordered, entry, -collectionAmount, BuildAdvanceDescription(entry.Description), ref running);
                    continue;
                }

                var appliedToDebt = Math.Min(running, collectionAmount);
                if (appliedToDebt > 0)
                {
                    AddStatementEntry(ordered, entry, -appliedToDebt, entry.Description, ref running);
                }

                var advanceAmount = collectionAmount - appliedToDebt;
                if (advanceAmount > 0)
                {
                    AddStatementEntry(ordered, entry, -advanceAmount, BuildAdvanceDescription(entry.Description), ref running);
                }

                continue;
            }

            AddStatementEntry(ordered, entry, entry.Amount, entry.Description, ref running);
        }

        return ordered;
    }

    private static void AddStatementEntry(
        List<StatementEntry> entries,
        StatementEntry source,
        decimal amount,
        string description,
        ref decimal running)
    {
        running += amount;
        entries.Add(new StatementEntry
        {
            Kind = source.Kind,
            Date = source.Date,
            Description = description,
            Amount = amount,
            RunningBalance = running
        });
    }

    private static string BuildAdvanceDescription(string description)
    {
        if (description.Contains("Avans", StringComparison.OrdinalIgnoreCase))
        {
            return description;
        }

        var text = description.Replace(" Tahsilatı", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        var separatorIndex = text.LastIndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex >= 0)
        {
            text = text[(separatorIndex + 3)..].Trim();
        }

        return $"{text} Avans Tahsilatı";
    }
}
