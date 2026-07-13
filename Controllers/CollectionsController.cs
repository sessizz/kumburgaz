using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[ModuleAuthorize(AppModules.Tahsilatlar)]
public class CollectionsController(
    ApplicationDbContext db,
    ICollectionService collectionService,
    ImportBatchService importBatchService) : Controller
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
        string? accountKey, string? note, bool isReceipt = false, string? referenceNo = null, string? returnUrl = null)
    {
        try
        {
            // Türkçe virgüllü tutarı destekle
            var rawAmount = (amount ?? "0").Trim().Replace(',', '.');
            if (!decimal.TryParse(rawAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedAmount) || parsedAmount <= 0)
            {
                TempData["ActionError"] = "Geçerli bir tutar giriniz.";
                return Redirect(returnUrl ?? Url.Action("Detail", "Units", new { id = unitId })!);
            }

            if (isReceipt && string.IsNullOrWhiteSpace(referenceNo))
            {
                TempData["ActionError"] = "Makbuz için makbuz no giriniz.";
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
                IsReceipt = isReceipt,
                ReferenceNo = referenceNo,
                Note = note
            };

            var collectionId = await collectionService.CreateAsync(model);
            TempData["ActionSuccess"] = $"{parsedAmount:N2} TL tahsilat kaydedildi.";

            if (isReceipt)
            {
                return RedirectToAction(nameof(PrintReceipt), new { id = collectionId, returnUrl });
            }
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
            IsReceipt = entity.IsReceipt,
            Note = entity.Note,
            ExistingAllocatedAmount = entity.Allocations.Sum(x => x.AppliedAmount),
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

    public async Task<IActionResult> PrintReceipt(int id, string? returnUrl = null)
    {
        var collection = await db.Collections
            .AsNoTracking()
            .Include(x => x.BillingGroup)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .ThenInclude(x => x!.Site)
            .Include(x => x.CashBox)
            .Include(x => x.BankAccount)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (collection is null)
        {
            return NotFound();
        }

        if (!collection.IsReceipt)
        {
            TempData["ActionError"] = "Bu tahsilat için makbuz oluşturulmamış.";
            return Redirect(returnUrl ?? Url.Action("Index", "Dues")!);
        }

        ViewBag.ReturnUrl = returnUrl;
        return View(collection);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? returnUrl = null)
    {
        // Mahsuplu gider islemine bagli tahsilat tekil silinemez; ikisi birlikte silinmeli.
        if (await db.MahsupIslemleri.AnyAsync(x => x.CollectionId == id))
        {
            TempData["ActionError"] = "Bu tahsilat bir mahsuplu gider işlemine bağlı, tekil silinemez. Mahsubu bütün olarak Mobil > Gider ekranından silin.";
            return RedirectAfterSave(returnUrl);
        }

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

        var importItems = new List<CollectionImportItem>();
        var seenKeys = new HashSet<string>();
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var lineNo = i + 1;
            ImportRowStatus status = ImportRowStatus.Ready;
            string? error = null;
            int billingGroupId = 0;
            DateTime date = default;
            decimal amount = 0m;
            PaymentChannel paymentChannel = PaymentChannel.Bank;

            var billingGroupIdText = ReadFirstValue(row, headers, "billinggroupid", "aidatgrubuid");
            if (!int.TryParse(billingGroupIdText, out billingGroupId) || !billingGroupSet.Contains(billingGroupId))
            {
                status = ImportRowStatus.Error;
                error = "Geçerli AidatGrubuId bulunamadı.";
            }

            if (status == ImportRowStatus.Ready &&
                !TryParseDate(ReadFirstValue(row, headers, "date", "tarih"), out date))
            {
                status = ImportRowStatus.Error;
                error = "Tarih alanı geçersiz.";
            }

            if (status == ImportRowStatus.Ready &&
                (!TryParseAmount(ReadFirstValue(row, headers, "amount", "tutar"), out amount) || amount <= 0))
            {
                status = ImportRowStatus.Error;
                error = "Tutar alanı geçersiz.";
            }

            if (status == ImportRowStatus.Ready &&
                !TryParsePaymentChannel(ReadFirstValue(row, headers, "paymentchannel", "odemekanali"), out paymentChannel))
            {
                status = ImportRowStatus.Error;
                error = "OdemeKanali alanı geçersiz.";
            }

            var referenceNo = NullIfWhiteSpace(ReadFirstValue(row, headers, "referenceno", "referansno"));
            var note = NullIfWhiteSpace(ReadFirstValue(row, headers, "note", "not"));
            var normalizedKey = ImportBatchService.BuildNormalizedKey(
                "collection",
                billingGroupId,
                date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                amount.ToString("0.00", CultureInfo.InvariantCulture),
                referenceNo ?? string.Empty);

            if (status == ImportRowStatus.Ready && !seenKeys.Add(normalizedKey))
            {
                status = ImportRowStatus.Duplicate;
                error = "Dosya içinde mükerrer satır.";
            }

            if (status == ImportRowStatus.Ready && await importBatchService.HasCommittedDuplicateAsync(normalizedKey))
            {
                status = ImportRowStatus.Duplicate;
                error = "Daha önce import edilmiş mükerrer kayıt.";
            }

            var model = new CollectionCreateViewModel
            {
                BillingGroupId = billingGroupId,
                Date = date,
                Amount = amount,
                PaymentChannel = paymentChannel,
                ReferenceNo = referenceNo,
                Note = note
            };

            importItems.Add(new CollectionImportItem(lineNo, row, model, normalizedKey, status, error));
        }

        var batch = await importBatchService.CreateAsync(
            "collection",
            null,
            null,
            file.FileName,
            await ComputeFileHashAsync(file));

        if (importItems.Any(x => x.Status != ImportRowStatus.Ready))
        {
            foreach (var item in importItems)
            {
                await importBatchService.AddRowAsync(
                    batch,
                    item.LineNo,
                    item.RawRow,
                    item.NormalizedKey,
                    item.Status,
                    item.ErrorMessage);
            }

            await importBatchService.FailAsync(batch);
            var errorCount = importItems.Count(x => x.Status == ImportRowStatus.Error);
            var duplicateCount = importItems.Count(x => x.Status == ImportRowStatus.Duplicate);
            TempData["ImportError"] = $"CSV import edilmedi. Batch: {batch.ImportNo}. Hatalı: {errorCount}, mükerrer: {duplicateCount}.";
            return RedirectToAction(nameof(Index));
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var item in importItems)
            {
                var collectionId = await collectionService.CreateAsync(item.Model);
                await importBatchService.AddRowAsync(
                    batch,
                    item.LineNo,
                    item.RawRow,
                    item.NormalizedKey,
                    ImportRowStatus.Committed,
                    createdEntityName: nameof(Collection),
                    createdEntityId: collectionId);
            }

            await importBatchService.CommitAsync(batch);
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            await importBatchService.FailAsync(batch);
            TempData["ImportError"] = $"CSV import edilmedi. Batch: {batch.ImportNo}. {ex.Message}";
            return RedirectToAction(nameof(Index));
        }

        TempData["ImportSuccess"] = $"{importItems.Count} tahsilat CSV ile eklendi. Batch: {batch.ImportNo}";
        return RedirectToAction(nameof(Index));
    }

    private static async Task<string> ComputeFileHashAsync(IFormFile file)
    {
        await using var stream = file.OpenReadStream();
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }

    private sealed record CollectionImportItem(
        int LineNo,
        string[] RawRow,
        CollectionCreateViewModel Model,
        string NormalizedKey,
        ImportRowStatus Status,
        string? ErrorMessage);

    private async Task<CollectionCreateViewModel> BuildFormAsync(CollectionCreateViewModel model)
    {
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

        var effectiveRemaining = OpeningBalanceCreditHelper.BuildEffectiveRemainingMap(installments);

        if (model.DuesInstallmentId.HasValue)
        {
            var selectedInstallment = installments.FirstOrDefault(x => x.Id == model.DuesInstallmentId.Value);

            if (selectedInstallment is not null)
            {
                model.BillingGroupId = selectedInstallment.BillingGroupId;
                var selectedRemaining = effectiveRemaining.GetValueOrDefault(selectedInstallment.Id, selectedInstallment.RemainingAmount)
                    + model.ExistingAllocatedAmount;
                if (model.Amount <= 0)
                {
                    model.Amount = selectedRemaining;
                }

                model.AllocationPreviewDebt = selectedRemaining;
                model.AllocationPreviewApplied = Math.Min(model.Amount, selectedRemaining);
                model.AllocationPreviewAdvance = Math.Max(0m, model.Amount - selectedRemaining);
            }
        }

        model.DuesInstallmentOptions = installments
            .Where(x => effectiveRemaining.GetValueOrDefault(x.Id, x.RemainingAmount) > 0
                        || (model.DuesInstallmentId.HasValue && x.Id == model.DuesInstallmentId.Value))
            .Select(x =>
            {
                var unitText = x.Unit is not null ? UnitDisplayHelper.Display(x.Unit) : BillingGroupDisplayHelper.UnitDisplay(x.BillingGroup);
                var duesType = x.BillingGroup?.DuesType?.Name ?? "Aidat";
                var responsible = string.IsNullOrWhiteSpace(x.ResponsibleAccount?.Name) ? "" : $" / {x.ResponsibleAccount.Name}";
                var remaining = effectiveRemaining.GetValueOrDefault(x.Id, x.RemainingAmount);
                var text = $"{x.Period} / {unitText}{responsible} / {duesType} / Kalan {remaining:N2} TL";
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
