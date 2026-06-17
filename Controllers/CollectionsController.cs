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
        return RedirectToAction("Index", "Dues");
    }

    public async Task<IActionResult> ExportCsv()
    {
        var rows = await collectionService.GetAllAsync();
        var csvRows = new List<string[]>
        {
            new[] { "AidatGrubuId", "AidatGrubu", "Tarih", "Tutar", "OdemeKanali", "ReferansNo", "Not" }
        };

        csvRows.AddRange(rows.Select(x => new[]
        {
            x.BillingGroupId.ToString(),
            x.BillingGroup?.Name ?? string.Empty,
            x.Date.ToString("yyyy-MM-dd"),
            x.Amount.ToString(CultureInfo.InvariantCulture),
            EnumDisplayHelper.Display(x.PaymentChannel),
            x.ReferenceNo ?? string.Empty,
            x.Note ?? string.Empty
        }));

        var bytes = CsvExportHelper.BuildCsv(csvRows.ToArray());
        return File(bytes, "text/csv; charset=utf-8", "tahsilatlar.csv");
    }

    public async Task<IActionResult> Create(int? duesInstallmentId = null, int? unitId = null, string? returnUrl = null)
    {
        var model = new CollectionCreateViewModel
        {
            DuesInstallmentId = duesInstallmentId,
            Date = DateTime.Today,
            PaymentChannel = PaymentChannel.Bank,
            ReturnUrl = returnUrl
        };

        // Daire seçildiyse ve henüz taksit belirtilmediyse, dairenin en eski açık taksitini seç
        if (unitId.HasValue && !duesInstallmentId.HasValue)
        {
            var openInstallment = await db.DuesInstallments.AsNoTracking()
                .Where(x => x.UnitId == unitId.Value && x.RemainingAmount > 0)
                .OrderBy(x => x.AccrualDate)
                .ThenBy(x => x.DueDate)
                .FirstOrDefaultAsync();
            if (openInstallment is not null)
            {
                model.DuesInstallmentId = openInstallment.Id;
            }
        }

        return View(await BuildFormAsync(model));
    }

    /// <summary>
    /// Daire detay/ekstre sayfasındaki "Tahsilat ekle" modalı için POST endpoint.
    /// BillingGroupId'yi dairenin en güncel grubundan otomatik çözer.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateForUnit(int unitId, string amount, DateTime date,
        string? accountKey, string? note, string? returnUrl)
    {
        try
        {
            if (!FlexibleDecimalParser.TryParse(amount, out var parsedAmount) || parsedAmount <= 0)
            {
                TempData["ActionError"] = "Geçerli bir tutar giriniz.";
                return Redirect(returnUrl ?? Url.Action("Detail", "Units", new { id = unitId })!);
            }
            var billingGroupId = await ResolveUnitBillingGroupIdAsync(unitId);
            if (billingGroupId is null)
            {
                TempData["ActionError"] = "Bu daire için aktif bir aidat grubu bulunamadı.";
                return Redirect(returnUrl ?? Url.Action("Detail", "Units", new { id = unitId })!);
            }

            // Dairenin en eski açık taksitine yönlendir (varsa)
            var targetInstallment = await db.DuesInstallments.AsNoTracking()
                .Where(x => x.UnitId == unitId && x.RemainingAmount > 0)
                .OrderBy(x => x.AccrualDate)
                .ThenBy(x => x.DueDate)
                .FirstOrDefaultAsync();

            var model = new CollectionCreateViewModel
            {
                BillingGroupId = billingGroupId.Value,
                DuesInstallmentId = targetInstallment?.Id,
                Date = date,
                Amount = parsedAmount,
                PaymentChannel = FinancialAccountHelper.TryParse(accountKey, out var ch, out _, out _) ? ch : PaymentChannel.Bank,
                AccountKey = accountKey,
                Note = note
            };

            await collectionService.CreateAsync(model);
            TempData["ActionSuccess"] = $"{parsedAmount:N2} TL tahsilat kaydedildi.";
        }
        catch (Exception ex)
        {
            TempData["ActionError"] = ex.Message;
        }

        return Redirect(returnUrl ?? Url.Action("Detail", "Units", new { id = unitId })!);
    }

    private async Task<int?> ResolveUnitBillingGroupIdAsync(int unitId)
    {
        var assignment = await db.BillingGroupUnits.AsNoTracking()
            .Where(x => x.UnitId == unitId && x.BillingGroup != null && x.BillingGroup.Active)
            .OrderByDescending(x => x.StartPeriod)
            .FirstOrDefaultAsync();
        return assignment?.BillingGroupId;
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
            return RedirectAfterSave(model.ReturnUrl);
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
            DuesInstallmentId = entity.Allocations
                .OrderBy(x => x.Id)
                .Select(x => (int?)x.DuesInstallmentId)
                .FirstOrDefault(),
            Date = entity.Date,
            Amount = entity.Amount,
            PaymentChannel = entity.PaymentChannel,
            AccountKey = FinancialAccountHelper.BuildKey(entity.CashBoxId, entity.BankAccountId),
            ReferenceNo = entity.ReferenceNo,
            Note = entity.Note,
            ReturnUrl = Request.Query["returnUrl"].FirstOrDefault()
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
            return RedirectAfterSave(model.ReturnUrl);
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
    public async Task<IActionResult> Delete(int id, string? returnUrl = null)
    {
        await collectionService.DeleteAsync(id);
        return RedirectAfterSave(returnUrl);
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
        if (!HasAnyHeader(headers, "billinggroupid", "aidatgrubuid") ||
            !HasAnyHeader(headers, "date", "tarih") ||
            !HasAnyHeader(headers, "amount", "tutar"))
        {
            TempData["ImportError"] = "Zorunlu alanlar: AidatGrubuId, Tarih, Tutar.";
            return RedirectToAction(nameof(Index));
        }

        var billingGroupIds = await db.BillingGroups.AsNoTracking().Select(x => x.Id).ToListAsync();
        var billingGroupSet = billingGroupIds.ToHashSet();

        var count = 0;
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var lineNo = i + 1;

            var billingGroupIdText = ReadFirstValue(row, headers, "billinggroupid", "aidatgrubuid");
            if (!int.TryParse(billingGroupIdText, out var billingGroupId) || !billingGroupSet.Contains(billingGroupId))
            {
                TempData["ImportError"] = $"Satir {lineNo}: geçerli AidatGrubuId bulunamadı.";
                return RedirectToAction(nameof(Index));
            }

            if (!TryParseDate(ReadFirstValue(row, headers, "date", "tarih"), out var date))
            {
                TempData["ImportError"] = $"Satir {lineNo}: Tarih alanı geçersiz.";
                return RedirectToAction(nameof(Index));
            }

            if (!TryParseAmount(ReadFirstValue(row, headers, "amount", "tutar"), out var amount) || amount <= 0)
            {
                TempData["ImportError"] = $"Satir {lineNo}: Tutar alanı geçersiz.";
                return RedirectToAction(nameof(Index));
            }

            if (!TryParsePaymentChannel(ReadFirstValue(row, headers, "paymentchannel", "odemekanali"), out var paymentChannel))
            {
                TempData["ImportError"] = $"Satir {lineNo}: OdemeKanali alanı geçersiz.";
                return RedirectToAction(nameof(Index));
            }

            var model = new CollectionCreateViewModel
            {
                BillingGroupId = billingGroupId,
                Date = date,
                Amount = amount,
                PaymentChannel = paymentChannel,
                ReferenceNo = NullIfWhiteSpace(ReadFirstValue(row, headers, "referenceno", "referansno")),
                Note = NullIfWhiteSpace(ReadFirstValue(row, headers, "note", "not"))
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
        if (model.DuesInstallmentId.HasValue)
        {
            var selectedInstallment = await db.DuesInstallments
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == model.DuesInstallmentId.Value);

            if (selectedInstallment is not null)
            {
                model.BillingGroupId = selectedInstallment.BillingGroupId;
                if (model.Amount <= 0)
                {
                    model.Amount = selectedInstallment.RemainingAmount;
                }
            }
        }

        var installments = await db.DuesInstallments
            .AsNoTracking()
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.DuesType)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.CombinedUnitMembers)
            .ThenInclude(x => x.ComponentUnit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.ResponsibleAccount)
            .Where(x => x.Unit == null || x.Unit.Active)
            .Where(x => x.RemainingAmount > 0 || (model.DuesInstallmentId.HasValue && x.Id == model.DuesInstallmentId.Value))
            .OrderBy(x => x.Period)
            .ThenBy(x => x.DueDate)
            .ThenBy(x => x.Unit!.Block!.Name)
            .ThenBy(x => x.Unit!.UnitNo)
            .ToListAsync();

        model.DuesInstallmentOptions = installments
            .Select(x =>
            {
                var unitText = x.Unit is not null ? UnitDisplayHelper.Display(x.Unit) : BillingGroupDisplayHelper.UnitDisplay(x.BillingGroup);
                var duesType = x.BillingGroup?.DuesType?.Name ?? "Aidat";
                var responsible = string.IsNullOrWhiteSpace(x.ResponsibleAccount?.Name) ? "" : $" / {x.ResponsibleAccount.Name}";
                var text = $"{x.Period} / {unitText}{responsible} / {duesType} / Kalan {x.RemainingAmount:N2} TL";
                return new SelectListItem(text, x.Id.ToString(), model.DuesInstallmentId == x.Id);
            })
            .ToList();

        model.AccountOptions = await FinancialAccountHelper.BuildOptionsAsync(db, model.AccountKey);

        var groups = await db.BillingGroups
            .AsNoTracking()
            .Where(x => x.Active)
            .Include(x => x.DuesType)
            .Include(x => x.Units)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.Units)
            .ThenInclude(x => x.Unit)
            .ThenInclude(x => x!.CombinedUnitMembers)
            .ThenInclude(x => x.ComponentUnit)
            .ThenInclude(x => x!.Block)
            .OrderBy(x => x.Name)
            .ToListAsync();

        model.BillingGroupOptions = groups
            .SelectMany(group =>
            {
                var units = group.Units
                    .Where(x => x.Unit is { Active: true })
                    .OrderBy(x => x.Unit!.Block!.Name)
                    .ThenBy(x => x.Unit!.UnitNo)
                    .Select(x => UnitDisplayHelper.Display(x.Unit))
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

    private IActionResult RedirectAfterSave(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction("Index", "Dues");
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

    private static string ReadFirstValue(string[] row, Dictionary<string, int> headers, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = ReadValue(row, headers, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static bool HasAnyHeader(Dictionary<string, int> headers, params string[] keys) =>
        keys.Any(headers.ContainsKey);

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
        return FlexibleDecimalParser.TryParse(value, out amount);
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
