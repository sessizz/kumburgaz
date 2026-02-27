using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize]
public class UnitsController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var units = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .ToListAsync();

        return View(units);
    }

    public async Task<IActionResult> ExportCsv()
    {
        var units = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .OrderBy(x => x.Block!.Name)
            .ThenBy(x => x.UnitNo)
            .ToListAsync();

        var rows = new List<string[]>
        {
            new[] { "BlockId", "Block", "UnitNo", "OwnerName", "Active" }
        };

        rows.AddRange(units.Select(x => new[]
        {
            x.BlockId.ToString(),
            x.Block?.Name ?? string.Empty,
            x.UnitNo,
            x.OwnerName ?? string.Empty,
            x.Active ? "true" : "false"
        }));

        var bytes = CsvExportHelper.BuildCsv(rows.ToArray());
        return File(bytes, "text/csv; charset=utf-8", "daireler.csv");
    }

    public async Task<IActionResult> Create()
    {
        await PopulateBlocksAsync();
        return View(new Unit { Active = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Unit model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateBlocksAsync();
            return View(model);
        }

        db.Units.Add(model);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var unit = await db.Units.FindAsync(id);
        if (unit is null)
        {
            return NotFound();
        }

        await PopulateBlocksAsync();
        return View(unit);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Unit model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateBlocksAsync();
            return View(model);
        }

        db.Units.Update(model);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await db.Units.FindAsync(id);
        if (entity is null)
        {
            TempData["ActionError"] = "Daire bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        var hasRelations = await db.BillingGroupUnits.AnyAsync(x => x.UnitId == id)
            || await db.Collections.AnyAsync(x => x.UnitId == id)
            || await db.DuesInstallments.AnyAsync(x => x.UnitId == id);

        if (hasRelations)
        {
            TempData["ActionError"] = "Daire bağlı kayıtlar içeriyor. Önce ilişkili kayıtları kaldırın.";
            return RedirectToAction(nameof(Index));
        }

        db.Units.Remove(entity);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Daire silindi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportCsv(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["ImportError"] = "CSV dosyası seciniz.";
            return RedirectToAction(nameof(Index));
        }

        var rows = await CsvImportHelper.ReadRowsAsync(file);
        if (rows.Count < 2)
        {
            TempData["ImportError"] = "CSV baslik ve en az bir veri satırı icermelidir.";
            return RedirectToAction(nameof(Index));
        }

        var headers = BuildHeaders(rows[0]);
        if (!headers.ContainsKey("unitno"))
        {
            TempData["ImportError"] = "Zorunlu alan eksik: UnitNo.";
            return RedirectToAction(nameof(Index));
        }

        if (!headers.ContainsKey("blockid") && !headers.ContainsKey("block"))
        {
            TempData["ImportError"] = "Zorunlu alan eksik: BlockId veya Block.";
            return RedirectToAction(nameof(Index));
        }

        var blocks = await db.Blocks.AsNoTracking().ToListAsync();
        var blockById = blocks.ToDictionary(x => x.Id);
        var blockByName = blocks.ToDictionary(x => NormalizeHeaderKey(x.Name), x => x.Id);

        var existingKeys = await db.Units.AsNoTracking()
            .Select(x => $"{x.BlockId}|{x.UnitNo.Trim().ToUpperInvariant()}")
            .ToListAsync();
        var seen = new HashSet<string>(existingKeys);

        var toAdd = new List<Unit>();
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var lineNo = i + 1;

            var unitNo = ReadValue(row, headers, "unitno");
            if (string.IsNullOrWhiteSpace(unitNo))
            {
                TempData["ImportError"] = $"Satir {lineNo}: UnitNo zorunludur.";
                return RedirectToAction(nameof(Index));
            }

            if (!TryResolveBlockId(row, headers, blockById, blockByName, out var blockId, out var blockError))
            {
                TempData["ImportError"] = $"Satir {lineNo}: {blockError}";
                return RedirectToAction(nameof(Index));
            }

            var key = $"{blockId}|{unitNo.Trim().ToUpperInvariant()}";
            if (!seen.Add(key))
            {
                TempData["ImportError"] = $"Satir {lineNo}: Bu blokta ayni daire no zaten var ({unitNo}).";
                return RedirectToAction(nameof(Index));
            }

            var ownerName = ReadValue(row, headers, "ownername");
            var active = ParseBool(ReadValue(row, headers, "active"), true);

            toAdd.Add(new Unit
            {
                BlockId = blockId,
                UnitNo = unitNo.Trim(),
                OwnerName = string.IsNullOrWhiteSpace(ownerName) ? null : ownerName.Trim(),
                Active = active
            });
        }

        db.Units.AddRange(toAdd);
        await db.SaveChangesAsync();
        TempData["ImportSuccess"] = $"{toAdd.Count} daire CSV ile eklendi.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateBlocksAsync()
    {
        ViewBag.Blocks = await db.Blocks.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString()))
            .ToListAsync();
    }

    private static Dictionary<string, int> BuildHeaders(string[] row)
    {
        var map = new Dictionary<string, int>();
        for (var i = 0; i < row.Length; i++)
        {
            var key = NormalizeHeaderKey(row[i]);
            if (!string.IsNullOrWhiteSpace(key))
            {
                map[key] = i;
            }
        }

        return map;
    }

    private static string ReadValue(string[] row, Dictionary<string, int> headers, string key)
    {
        if (!headers.TryGetValue(key, out var idx) || idx >= row.Length)
        {
            return string.Empty;
        }

        return row[idx].Trim();
    }

    private static string NormalizeHeaderKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty);
    }

    private static bool TryResolveBlockId(
        string[] row,
        Dictionary<string, int> headers,
        Dictionary<int, Block> blockById,
        Dictionary<string, int> blockByName,
        out int blockId,
        out string error)
    {
        blockId = 0;
        var blockIdText = ReadValue(row, headers, "blockid");
        if (!string.IsNullOrWhiteSpace(blockIdText))
        {
            if (!int.TryParse(blockIdText, out blockId) || !blockById.ContainsKey(blockId))
            {
                error = "geçerli BlockId bulunamadı.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        var blockName = NormalizeHeaderKey(ReadValue(row, headers, "block"));
        if (!string.IsNullOrWhiteSpace(blockName) && blockByName.TryGetValue(blockName, out blockId))
        {
            error = string.Empty;
            return true;
        }

        error = "geçerli Block alanı bulunamadı.";
        return false;
    }

    private static bool ParseBool(string value, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is "1" or "true" or "evet" or "yes" or "aktif")
        {
            return true;
        }

        if (normalized is "0" or "false" or "hayir" or "no" or "pasif")
        {
            return false;
        }

        return fallback;
    }
}
