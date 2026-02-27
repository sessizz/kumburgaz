using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize]
public class CollectionsController(
    ApplicationDbContext db,
    ICollectionService collectionService) : Controller
{
    public async Task<IActionResult> Index()
    {
        return RedirectToAction("Index", "Ledger");
    }

    public async Task<IActionResult> ExportCsv()
    {
        var rows = await collectionService.GetAllAsync();
        var csvRows = new List<string[]>
        {
            new[] { "BillingGroupId", "BillingGroup", "Date", "Amount", "PaymentChannel", "ReferenceNo", "Note" }
        };

        csvRows.AddRange(rows.Select(x => new[]
        {
            x.BillingGroupId.ToString(),
            x.BillingGroup?.Name ?? string.Empty,
            x.Date.ToString("yyyy-MM-dd"),
            x.Amount.ToString(CultureInfo.InvariantCulture),
            x.PaymentChannel.ToString(),
            x.ReferenceNo ?? string.Empty,
            x.Note ?? string.Empty
        }));

        var bytes = CsvExportHelper.BuildCsv(csvRows.ToArray());
        return File(bytes, "text/csv; charset=utf-8", "tahsilatlar.csv");
    }

    public async Task<IActionResult> Create()
    {
        return View(await BuildFormAsync(new CollectionCreateViewModel
        {
            Date = DateTime.Today,
            PaymentChannel = PaymentChannel.Bank
        }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CollectionCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(model));
        }

        try
        {
            await collectionService.CreateAsync(model);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(await BuildFormAsync(model));
        }
    }

    public async Task<IActionResult> Edit(int id)
    {
        var entity = await collectionService.GetByIdAsync(id);
        if (entity is null)
        {
            return NotFound();
        }

        var model = new CollectionCreateViewModel
        {
            BillingGroupId = entity.BillingGroupId,
            Date = entity.Date,
            Amount = entity.Amount,
            PaymentChannel = entity.PaymentChannel,
            ReferenceNo = entity.ReferenceNo,
            Note = entity.Note
        };

        ViewBag.CollectionId = id;
        return View(await BuildFormAsync(model));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CollectionCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.CollectionId = id;
            return View(await BuildFormAsync(model));
        }

        try
        {
            await collectionService.UpdateAsync(id, model);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ViewBag.CollectionId = id;
            return View(await BuildFormAsync(model));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        await collectionService.DeleteAsync(id);
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
        if (!headers.ContainsKey("billinggroupid") || !headers.ContainsKey("date") || !headers.ContainsKey("amount"))
        {
            TempData["ImportError"] = "Zorunlu alanlar: BillingGroupId, Date, Amount.";
            return RedirectToAction(nameof(Index));
        }

        var billingGroupIds = await db.BillingGroups.AsNoTracking().Select(x => x.Id).ToListAsync();
        var billingGroupSet = billingGroupIds.ToHashSet();

        var count = 0;
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var lineNo = i + 1;

            var billingGroupIdText = ReadValue(row, headers, "billinggroupid");
            if (!int.TryParse(billingGroupIdText, out var billingGroupId) || !billingGroupSet.Contains(billingGroupId))
            {
                TempData["ImportError"] = $"Satir {lineNo}: geçerli BillingGroupId bulunamadı.";
                return RedirectToAction(nameof(Index));
            }

            if (!TryParseDate(ReadValue(row, headers, "date"), out var date))
            {
                TempData["ImportError"] = $"Satir {lineNo}: Date alanı geçersiz.";
                return RedirectToAction(nameof(Index));
            }

            if (!TryParseAmount(ReadValue(row, headers, "amount"), out var amount) || amount <= 0)
            {
                TempData["ImportError"] = $"Satir {lineNo}: Amount alanı geçersiz.";
                return RedirectToAction(nameof(Index));
            }

            if (!TryParsePaymentChannel(ReadValue(row, headers, "paymentchannel"), out var paymentChannel))
            {
                TempData["ImportError"] = $"Satir {lineNo}: PaymentChannel alanı geçersiz.";
                return RedirectToAction(nameof(Index));
            }

            var model = new CollectionCreateViewModel
            {
                BillingGroupId = billingGroupId,
                Date = date,
                Amount = amount,
                PaymentChannel = paymentChannel,
                ReferenceNo = NullIfWhiteSpace(ReadValue(row, headers, "referenceno")),
                Note = NullIfWhiteSpace(ReadValue(row, headers, "note"))
            };

            try
            {
                await collectionService.CreateAsync(model);
                count++;
            }
            catch (Exception ex)
            {
                TempData["ImportError"] = $"Satir {lineNo}: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        TempData["ImportSuccess"] = $"{count} tahsilat CSV ile eklendi.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<CollectionCreateViewModel> BuildFormAsync(CollectionCreateViewModel model)
    {
        var groups = await db.BillingGroups
            .AsNoTracking()
            .Where(x => x.Active)
            .Include(x => x.DuesType)
            .Include(x => x.Units)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .OrderBy(x => x.Name)
            .ToListAsync();

        model.BillingGroupOptions = groups
            .SelectMany(group =>
            {
                var units = group.Units
                    .Where(x => x.Unit?.Block is not null)
                    .OrderBy(x => x.Unit!.Block!.Name)
                    .ThenBy(x => x.Unit!.UnitNo)
                    .Select(x => $"{x.Unit!.Block!.Name}-{x.Unit.UnitNo}")
                    .ToList();

                if (units.Count == 0)
                {
                    return [];
                }

                var duesType = group.DuesType?.Name ?? string.Empty;
                if (group.IsMerged || units.Count == 1)
                {
                    return
                    [
                        new SelectListItem($"{string.Join(" + ", units)} / {duesType}", group.Id.ToString())
                    ];
                }

                return units
                    .Select(unit => new SelectListItem($"{unit} / {duesType}", group.Id.ToString()))
                    .ToList();
            })
            .OrderBy(x => x.Text)
            .ToList();

        return model;
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

    private static string? NullIfWhiteSpace(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool TryParseDate(string value, out DateTime date)
    {
        var formats = new[] { "yyyy-MM-dd", "dd.MM.yyyy", "dd/MM/yyyy", "MM/dd/yyyy" };
        return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateTime.TryParse(value, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out date)
            || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryParseAmount(string value, out decimal amount)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("tr-TR"), out amount)
            || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    private static bool TryParsePaymentChannel(string value, out PaymentChannel channel)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            channel = PaymentChannel.Bank;
            return true;
        }

        if (int.TryParse(value, out var asInt) && Enum.IsDefined(typeof(PaymentChannel), asInt))
        {
            channel = (PaymentChannel)asInt;
            return true;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is "cash" or "nakit")
        {
            channel = PaymentChannel.Cash;
            return true;
        }

        if (normalized is "bank" or "banka" or "havale" or "eft")
        {
            channel = PaymentChannel.Bank;
            return true;
        }

        channel = default;
        return false;
    }
}
