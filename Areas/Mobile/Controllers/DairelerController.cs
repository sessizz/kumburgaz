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
    public async Task<IActionResult> Index(string? q = null)
    {
        var term = q?.Trim();

        var unitsQuery = db.Units.AsNoTracking()
            .Include(x => x.Block)
            .Where(x => x.Active);

        if (!string.IsNullOrWhiteSpace(term))
        {
            unitsQuery = unitsQuery.Where(x =>
                x.UnitNo.Contains(term)
                || (x.Block != null && x.Block.Name.Contains(term))
                || (x.OwnerName != null && x.OwnerName.Contains(term)));
        }

        var units = await unitsQuery
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .ToListAsync();

        // Daire basina net bakiye (pozitif = borc). Masaustu borc raporuyla ayni kaynak.
        var balanceByUnit = (await reportingService.GetDuesDebtReportAsync(new DuesDebtReportQuery()))
            .Where(x => x.UnitId.HasValue)
            .GroupBy(x => x.UnitId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.RemainingAmount));

        var blocks = units
            .GroupBy(x => x.Block?.Name ?? "Blok Yok")
            .OrderBy(g => g.Key)
            .Select(g => new MobileUnitBlockGroup
            {
                BlockName = g.Key,
                Units = g.Select(x => new MobileUnitListItem
                {
                    Id = x.Id,
                    UnitNo = x.UnitNo,
                    Display = x.Block is null ? x.UnitNo : $"{x.Block.Name}-{x.UnitNo}",
                    OwnerName = x.OwnerName,
                    Balance = balanceByUnit.GetValueOrDefault(x.Id)
                }).ToList()
            })
            .ToList();

        return View(new MobileUnitListViewModel
        {
            Query = term,
            TotalCount = units.Count,
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
}
