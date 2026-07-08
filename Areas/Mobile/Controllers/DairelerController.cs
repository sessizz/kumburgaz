using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Areas.Mobile.Controllers;

[Area("Mobile")]
[ModuleAuthorize(AppModules.Daireler)]
public class DairelerController(
    ApplicationDbContext db,
    IReportingService reportingService,
    UnitStatementService statementService,
    UnitLedgerService unitLedgerService) : Controller
{
    public async Task<IActionResult> Index(string? q = null, string status = "all")
    {
        var term = q?.Trim();
        status = NormalizeStatus(status);

        var units = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .Where(x => x.Active)
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .ToListAsync();

        // Daire basina net bakiye (pozitif = borc). Masaustu borc raporuyla ayni kaynak.
        var debtRows = (await reportingService.GetDuesDebtReportAsync(new DuesDebtReportQuery()))
            .Where(x => x.UnitId.HasValue)
            .GroupBy(x => x.UnitId!.Value)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    Balance = g.Sum(x => x.RemainingAmount),
                    ResponsibleAccountName = g.Select(x => x.ResponsibleAccountName)
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                });

        var items = units.Select(x =>
            {
                debtRows.TryGetValue(x.Id, out var debt);
                return new MobileUnitListItem
                {
                    Id = x.Id,
                    UnitNo = x.UnitNo,
                    Display = x.Block is null ? x.UnitNo : $"{x.Block.Name}-{x.UnitNo}",
                    OwnerName = x.OwnerName,
                    ResponsibleAccountName = debt?.ResponsibleAccountName,
                    Balance = debt?.Balance ?? 0m
                };
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(term))
        {
            items = items.Where(x =>
                    Contains(x.UnitNo, term)
                    || Contains(x.Display, term)
                    || Contains(x.OwnerName, term)
                    || Contains(x.ResponsibleAccountName, term))
                .ToList();
        }

        var debtorCount = items.Count(x => x.Balance > 0.005m);
        var creditorCount = items.Count(x => x.Balance < -0.005m);
        var cleanCount = items.Count(x => Math.Abs(x.Balance) <= 0.005m);

        items = status switch
        {
            "debt" => items.Where(x => x.Balance > 0.005m).ToList(),
            "credit" => items.Where(x => x.Balance < -0.005m).ToList(),
            "clean" => items.Where(x => Math.Abs(x.Balance) <= 0.005m).ToList(),
            _ => items
        };

        var blocks = items
            .GroupBy(x => BlockNameFromDisplay(x.Display))
            .OrderBy(g => g.Key)
            .Select(g => new MobileUnitBlockGroup
            {
                BlockName = g.Key,
                Units = g.ToList()
            })
            .ToList();

        return View(new MobileUnitListViewModel
        {
            Query = term,
            Status = status,
            TotalCount = items.Count,
            DebtorCount = debtorCount,
            CreditorCount = creditorCount,
            CleanCount = cleanCount,
            Blocks = blocks
        });
    }

    public async Task<IActionResult> Detay(int id)
    {
        var unit = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (unit is null)
        {
            return NotFound();
        }

        var entries = await statementService.BuildAsync(id);
        var ledger = await unitLedgerService.BuildAsync(id);
        var balance = entries.Count > 0 ? entries[^1].RunningBalance : 0m;

        var accruals = entries
            .Where(x => x.Kind != StatementEntryKind.Collection)
            .OrderByDescending(x => x.Date)
            .ToList();
        var collections = entries
            .Where(x => x.Kind == StatementEntryKind.Collection)
            .OrderByDescending(x => x.Date)
            .ToList();

        return View(new MobileUnitDetailViewModel
        {
            Unit = unit,
            Balance = balance,
            Summary = ledger?.Summary ?? new UnitLedgerSummary(),
            Accruals = accruals,
            Collections = collections
        });
    }

    private static string NormalizeStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "debt" => "debt",
            "credit" => "credit",
            "clean" => "clean",
            _ => "all"
        };
    }

    private static bool Contains(string? value, string term)
    {
        return value?.Contains(term, StringComparison.CurrentCultureIgnoreCase) == true;
    }

    private static string BlockNameFromDisplay(string display)
    {
        var separator = display.IndexOf('-', StringComparison.Ordinal);
        return separator > 0 ? display[..separator].Trim() : "Blok Yok";
    }
}
