using System.Globalization;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[ModuleAuthorize(AppModules.Muhasebe)]
public class OpeningBalancesController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(int? blockId = null)
    {
        var blocks = await db.Blocks.AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync();

        var unitsQuery = db.Units.AsNoTracking()
            .Include(x => x.Block)
            .Where(x => x.Active);

        if (blockId.HasValue)
            unitsQuery = unitsQuery.Where(x => x.BlockId == blockId.Value);

        var units = await unitsQuery
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .ToListAsync();

        ViewBag.Blocks = blocks;
        ViewBag.SelectedBlockId = blockId;
        return View(units);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int[] unitIds, string[] balances, string[] dates, int? blockId = null)
    {
        if (unitIds is null || balances is null || dates is null
            || unitIds.Length != balances.Length || unitIds.Length != dates.Length)
        {
            TempData["ActionError"] = "Geçersiz form verisi.";
            return RedirectToAction(nameof(Index), new { blockId });
        }

        var ids = unitIds.ToList();
        var units = await db.Units.Where(x => ids.Contains(x.Id)).ToListAsync();
        var unitMap = units.ToDictionary(x => x.Id);

        var changedCount = 0;
        var invalidCount = 0;
        for (var i = 0; i < unitIds.Length; i++)
        {
            if (!unitMap.TryGetValue(unitIds[i], out var unit)) continue;

            var raw = (balances[i] ?? string.Empty).Trim().Replace(',', '.');
            if (string.IsNullOrWhiteSpace(raw)) raw = "0";

            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var newValue))
            {
                invalidCount++;
                continue;
            }

            // Tarih (yyyy-MM-dd; HTML date input formatı). PostgreSQL timestamptz UTC ister.
            DateTime? newDate = null;
            var rawDate = (dates[i] ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(rawDate))
            {
                if (DateTime.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    newDate = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
                else if (DateTime.TryParse(rawDate, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out var parsedTr))
                    newDate = DateTime.SpecifyKind(parsedTr.Date, DateTimeKind.Utc);
            }

            // Bakiye 0 ise tarih de temizlenir
            if (newValue == 0m) newDate = null;

            var changed = false;
            if (unit.OpeningBalance != newValue) { unit.OpeningBalance = newValue; changed = true; }
            if (unit.OpeningBalanceDate != newDate) { unit.OpeningBalanceDate = newDate; changed = true; }
            if (changed) changedCount++;
        }

        if (changedCount > 0)
            await db.SaveChangesAsync();

        var msg = $"{changedCount} dairenin devir bakiyesi güncellendi.";
        if (invalidCount > 0)
            msg += $" {invalidCount} satırda geçersiz tutar atlandı.";
        TempData["ActionSuccess"] = msg;
        return RedirectToAction(nameof(Index), new { blockId });
    }
}
