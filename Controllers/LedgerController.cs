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

[ModuleAuthorize(AppModules.Muhasebe)]
public class LedgerController(
    ApplicationDbContext db,
    ImportBatchService importBatchService,
    ImageAttachmentService imageAttachmentService,
    DocumentFileService documentFileService,
    CaptureSessionService captureSessions) : Controller
{
    public async Task<IActionResult> Index(int[] categoryIds, int? categoryId, DateTime? startDate, DateTime? endDate)
    {
        var selectedCategoryIds = NormalizeCategoryIds(categoryIds, categoryId);
        var rows = await BuildExpenseQuery(selectedCategoryIds, startDate, endDate)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        var ledgerIds = rows.Select(x => x.Id).ToList();

        return View(new LedgerIndexViewModel
        {
            CategoryId = selectedCategoryIds.Count == 1 ? selectedCategoryIds[0] : null,
            CategoryIds = selectedCategoryIds,
            StartDate = startDate,
            EndDate = endDate,
            CategoryOptions = await BuildExpenseCategoryOptionsAsync(selectedCategoryIds),
            Rows = rows,
            CategorySummaryRows = BuildCategorySummaryRows(rows),
            AttachmentsByLedgerId = await BuildAttachmentMapAsync(ledgerIds),
            MahsupUnitByLedgerId = await BuildMahsupUnitMapAsync(ledgerIds)
        });
    }

    public async Task<IActionResult> ExportCsv(int[] categoryIds, int? categoryId, DateTime? startDate, DateTime? endDate)
    {
        var selectedCategoryIds = NormalizeCategoryIds(categoryIds, categoryId);
        var rows = await BuildExpenseQuery(selectedCategoryIds, startDate, endDate)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        var csvRows = new List<string[]>
        {
            new[] { "GiderKategoriId", "KategoriTipi", "KategoriAdi", "Tarih", "Tutar", "OdemeKanali", "Aciklama" }
        };

        csvRows.AddRange(rows.Select(x => new[]
        {
            x.IncomeExpenseCategoryId?.ToString() ?? string.Empty,
            x.IncomeExpenseCategory?.Type ?? string.Empty,
            x.IncomeExpenseCategory?.Name ?? string.Empty,
            x.Date.ToString("yyyy-MM-dd"),
            x.Amount.ToString(CultureInfo.InvariantCulture),
            EnumDisplayHelper.Display(x.PaymentChannel),
            x.Description ?? string.Empty
        }));

        var bytes = CsvExportHelper.BuildCsv(csvRows.ToArray());
        return File(bytes, "text/csv; charset=utf-8", "giderler.csv");
    }

    public async Task<IActionResult> Income(int? categoryId, DateTime? startDate, DateTime? endDate)
    {
        var rows = await BuildIncomeQuery(categoryId, startDate, endDate)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        return View(new LedgerIndexViewModel
        {
            CategoryId = categoryId,
            StartDate = startDate,
            EndDate = endDate,
            CategoryOptions = await BuildIncomeCategoryOptionsAsync(categoryId),
            Rows = rows,
            CategorySummaryRows = BuildCategorySummaryRows(rows)
        });
    }

    public async Task<IActionResult> ExportIncomeCsv(int? categoryId, DateTime? startDate, DateTime? endDate)
    {
        var rows = await BuildIncomeQuery(categoryId, startDate, endDate)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        var csvRows = new List<string[]>
        {
            new[] { "GelirKategoriId", "KategoriTipi", "KategoriAdi", "Tarih", "Tutar", "OdemeKanali", "Aciklama" }
        };

        csvRows.AddRange(rows.Select(x => new[]
        {
            x.IncomeExpenseCategoryId?.ToString() ?? string.Empty,
            x.IncomeExpenseCategory?.Type ?? string.Empty,
            x.IncomeExpenseCategory?.Name ?? string.Empty,
            x.Date.ToString("yyyy-MM-dd"),
            x.Amount.ToString(CultureInfo.InvariantCulture),
            EnumDisplayHelper.Display(x.PaymentChannel),
            x.Description ?? string.Empty
        }));

        var bytes = CsvExportHelper.BuildCsv(csvRows.ToArray());
        return File(bytes, "text/csv; charset=utf-8", "gelirler.csv");
    }

    public async Task<IActionResult> Create()
    {
        return View(await BuildAsync(new LedgerTransactionCreateViewModel()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LedgerTransactionCreateViewModel model, List<IFormFile> Fotograflar, string? captureToken = null)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.CaptureToken = captureToken;
            return View(await BuildAsync(model));
        }

        var entity = new LedgerTransaction
        {
            Date = DateTimeHelper.EnsureUtc(model.Date),
            IncomeExpenseCategoryId = model.IncomeExpenseCategoryId,
            Amount = model.Amount,
            PaymentChannel = ResolvePaymentChannel(model, out var cashBoxId, out var bankAccountId),
            CashBoxId = cashBoxId,
            BankAccountId = bankAccountId,
            Description = model.Description
        };
        db.LedgerTransactions.Add(entity);
        await db.SaveChangesAsync();

        var attachmentErrors = await SaveAttachmentsAsync(entity.Id, Fotograflar);
        await SaveCapturedAttachmentsAsync(entity.Id, captureToken);
        if (attachmentErrors.Count > 0)
        {
            TempData["ActionError"] = "Kayıt oluşturuldu ama bazı dosyalar eklenemedi: " + string.Join(" ", attachmentErrors);
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var entity = await db.LedgerTransactions
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsTransfer && x.IncomeExpenseCategoryId.HasValue);
        if (entity is null)
        {
            return NotFound();
        }

        var categoryId = entity.IncomeExpenseCategoryId.GetValueOrDefault();
        var model = new LedgerTransactionCreateViewModel
        {
            Date = entity.Date,
            IncomeExpenseCategoryId = categoryId,
            Amount = entity.Amount,
            PaymentChannel = entity.PaymentChannel,
            AccountKey = FinancialAccountHelper.BuildKey(entity.CashBoxId, entity.BankAccountId),
            Description = entity.Description,
            ExistingAttachments = await BuildAttachmentSummariesAsync(id)
        };

        ViewBag.TransactionId = id;
        return View(await BuildAsync(model));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, LedgerTransactionCreateViewModel model, List<IFormFile> Fotograflar, string? captureToken = null)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.TransactionId = id;
            ViewBag.CaptureToken = captureToken;
            model.ExistingAttachments = await BuildAttachmentSummariesAsync(id);
            return View(await BuildAsync(model));
        }

        var entity = await db.LedgerTransactions.FirstOrDefaultAsync(x => x.Id == id && !x.IsTransfer);
        if (entity is null)
        {
            return NotFound();
        }

        entity.Date = DateTimeHelper.EnsureUtc(model.Date);
        entity.IncomeExpenseCategoryId = model.IncomeExpenseCategoryId;
        entity.Amount = model.Amount;
        entity.PaymentChannel = ResolvePaymentChannel(model, out var cashBoxId, out var bankAccountId);
        entity.CashBoxId = cashBoxId;
        entity.BankAccountId = bankAccountId;
        entity.Description = model.Description;

        await db.SaveChangesAsync();
        var attachmentErrors = await SaveAttachmentsAsync(id, Fotograflar);
        await SaveCapturedAttachmentsAsync(id, captureToken);
        if (attachmentErrors.Count > 0)
        {
            TempData["ActionError"] = "Kayıt güncellendi ama bazı dosyalar eklenemedi: " + string.Join(" ", attachmentErrors);
        }

        return RedirectToAction(nameof(Index));
    }

    // Gider/gelir fisine eklenmis fotografi gosterir.
    public async Task<IActionResult> Ek(int id)
    {
        var attachment = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (attachment is null)
        {
            return NotFound();
        }

        return File(attachment.Content, attachment.ContentType, attachment.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EkSil(int attachmentId, int ledgerTransactionId)
    {
        var attachment = await db.Attachments
            .FirstOrDefaultAsync(x => x.Id == attachmentId && x.EntityType == nameof(LedgerTransaction) && x.EntityId == ledgerTransactionId);
        if (attachment is not null)
        {
            db.Attachments.Remove(attachment);
            await db.SaveChangesAsync();
            TempData["ActionSuccess"] = "Fotoğraf kaldırıldı.";
        }

        return RedirectToAction(nameof(Edit), new { id = ledgerTransactionId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await db.LedgerTransactions.FindAsync(id);
        if (entity is null)
        {
            TempData["ActionError"] = "Kayıt bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        // Mahsuplu gider islemine bagli gider tekil silinemez; ikisi birlikte silinmeli.
        if (await db.MahsupIslemleri.AnyAsync(x => x.LedgerTransactionId == id))
        {
            TempData["ActionError"] = "Bu gider bir mahsuplu gider işlemine bağlı, tekil silinemez. Mahsubu bütün olarak Mobil > Gider ekranından silin.";
            return RedirectToAction(nameof(Index));
        }

        db.LedgerTransactions.Remove(entity);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "İşlem silindi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportCsv(IFormFile? file)
    {
        return await ImportLedgerCsvAsync(
            file,
            CategoryTypeHelper.Gider,
            nameof(Index),
            ["incomeexpensecategoryid", "giderkategoriid"],
            "GiderKategoriId",
            "gider",
            "gider kaydı");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportIncomeCsv(IFormFile? file)
    {
        return await ImportLedgerCsvAsync(
            file,
            CategoryTypeHelper.Gelir,
            nameof(Income),
            ["incomeexpensecategoryid", "gelirkategoriid"],
            "GelirKategoriId",
            "gelir",
            "gelir kaydı");
    }

    private async Task<IActionResult> ImportLedgerCsvAsync(
        IFormFile? file,
        string categoryType,
        string redirectAction,
        string[] categoryHeaderKeys,
        string categoryHeaderLabel,
        string batchType,
        string rowLabel)
    {
        if (file is null || file.Length == 0)
        {
            TempData["ImportError"] = "CSV dosyası seciniz.";
            return RedirectToAction(redirectAction);
        }

        var rows = await CsvImportHelper.ReadRowsAsync(file);
        if (rows.Count < 2)
        {
            TempData["ImportError"] = "CSV baslik ve en az bir veri satırı icermelidir.";
            return RedirectToAction(redirectAction);
        }

        var headers = BuildHeaders(rows[0]);
        if (!HasAnyHeader(headers, categoryHeaderKeys) ||
            !HasAnyHeader(headers, "date", "tarih") ||
            !HasAnyHeader(headers, "amount", "tutar"))
        {
            TempData["ImportError"] = $"Zorunlu alanlar: {categoryHeaderLabel}, Tarih, Tutar.";
            return RedirectToAction(redirectAction);
        }

        var categoryIds = await db.IncomeExpenseCategories
            .AsNoTracking()
            .Where(x => x.Type == categoryType)
            .Select(x => x.Id)
            .ToListAsync();
        var categorySet = categoryIds.ToHashSet();

        var importItems = new List<LedgerImportItem>();
        var seenKeys = new HashSet<string>();
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var lineNo = i + 1;
            ImportRowStatus status = ImportRowStatus.Ready;
            string? error = null;
            var categoryId = 0;
            DateTime date = default;
            decimal amount = 0m;
            PaymentChannel paymentChannel = PaymentChannel.Bank;
            int? cashBoxId = null;
            int? bankAccountId = null;

            var categoryIdText = ReadFirstValue(row, headers, categoryHeaderKeys);
            if (!int.TryParse(categoryIdText, out categoryId) || !categorySet.Contains(categoryId))
            {
                status = ImportRowStatus.Error;
                error = $"Geçerli {CategoryTypeHelper.Display(categoryType).ToLowerInvariant()} kategorisi bulunamadı.";
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

            var accountKey = NullIfWhiteSpace(ReadFirstValue(row, headers, "accountkey", "hesap", "kasabanka"));
            if (status == ImportRowStatus.Ready && !string.IsNullOrWhiteSpace(accountKey))
            {
                if (!FinancialAccountHelper.TryParse(accountKey, out paymentChannel, out cashBoxId, out bankAccountId))
                {
                    status = ImportRowStatus.Error;
                    error = "Hesap alanı geçersiz.";
                }
            }

            if (status == ImportRowStatus.Ready &&
                string.IsNullOrWhiteSpace(accountKey) &&
                !TryParsePaymentChannel(ReadFirstValue(row, headers, "paymentchannel", "odemekanali"), out paymentChannel))
            {
                status = ImportRowStatus.Error;
                error = "OdemeKanali alanı geçersiz.";
            }

            var description = NullIfWhiteSpace(ReadFirstValue(row, headers, "description", "aciklama"));
            var normalizedKey = ImportBatchService.BuildNormalizedKey(
                "ledger",
                categoryType,
                categoryId,
                accountKey ?? paymentChannel.ToString(),
                date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                amount.ToString("0.00", CultureInfo.InvariantCulture),
                description ?? string.Empty);

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

            var transaction = new LedgerTransaction
            {
                Date = DateTimeHelper.EnsureUtc(date),
                IncomeExpenseCategoryId = categoryId,
                Amount = amount,
                PaymentChannel = paymentChannel,
                CashBoxId = cashBoxId,
                BankAccountId = bankAccountId,
                Description = description
            };

            importItems.Add(new LedgerImportItem(lineNo, row, transaction, normalizedKey, status, error));
        }

        var batch = await importBatchService.CreateAsync(
            batchType,
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
            return RedirectToAction(redirectAction);
        }

        await using var dbTransaction = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var item in importItems)
            {
                db.LedgerTransactions.Add(item.Transaction);
                await db.SaveChangesAsync();
                await importBatchService.AddRowAsync(
                    batch,
                    item.LineNo,
                    item.RawRow,
                    item.NormalizedKey,
                    ImportRowStatus.Committed,
                    createdEntityName: nameof(LedgerTransaction),
                    createdEntityId: item.Transaction.Id);
            }

            await importBatchService.CommitAsync(batch);
            await dbTransaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync();
            await importBatchService.FailAsync(batch);
            TempData["ImportError"] = $"CSV import edilmedi. Batch: {batch.ImportNo}. {ex.Message}";
            return RedirectToAction(redirectAction);
        }

        TempData["ImportSuccess"] = $"{importItems.Count} {rowLabel} CSV ile eklendi. Batch: {batch.ImportNo}";
        return RedirectToAction(redirectAction);
    }

    private static async Task<string> ComputeFileHashAsync(IFormFile file)
    {
        await using var stream = file.OpenReadStream();
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }

    private sealed record LedgerImportItem(
        int LineNo,
        string[] RawRow,
        LedgerTransaction Transaction,
        string NormalizedKey,
        ImportRowStatus Status,
        string? ErrorMessage);

    private async Task<LedgerTransactionCreateViewModel> BuildAsync(LedgerTransactionCreateViewModel model)
    {
        model.CategoryOptions = await BuildExpenseCategoryOptionsAsync(NormalizeCategoryIds([], model.IncomeExpenseCategoryId));

        model.AccountOptions = await FinancialAccountHelper.BuildOptionsAsync(db, model.AccountKey);

        return model;
    }

    /// <summary>
    /// Yuklenen dosyalari turune gore isler: resim uzantilariysa
    /// ImageAttachmentService ile sikistirilir (fis fotografi davranisi degismez),
    /// digerleri (pdf/docx/xlsx/xls/csv/txt) DocumentFileService ile Belge ile ayni
    /// allowlist/boyut sinirinda dogrulanip ham bayt olarak eklenir. Basarisiz olan
    /// dosyalar (desteklenmeyen tur, bozuk resim vb.) kaydi bozmadan atlanir; hata
    /// mesajlari cagirana donulur ki kullaniciya gosterilebilsin.
    /// </summary>
    private async Task<List<string>> SaveAttachmentsAsync(int ledgerTransactionId, List<IFormFile>? files)
    {
        var errors = new List<string>();
        var uploaded = (files ?? []).Where(f => f.Length > 0).ToList();
        if (uploaded.Count == 0)
        {
            return errors;
        }

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var userName = User.FindFirst(ApplicationUserClaimsPrincipalFactory.DisplayNameClaimType)?.Value;

        foreach (var file in uploaded)
        {
            try
            {
                var extension = Path.GetExtension(file.FileName);
                string fileName, contentType;
                byte[] content;

                if (ImageAttachmentService.SupportedExtensions.Contains(extension))
                {
                    var compressed = await imageAttachmentService.CompressAsync(file);
                    fileName = compressed.FileName;
                    contentType = compressed.ContentType;
                    content = compressed.Content;
                }
                else
                {
                    var validated = await documentFileService.ValidateAsync(file);
                    if (!validated.IsValid)
                    {
                        errors.Add($"{file.FileName}: {validated.ErrorMessage}");
                        continue;
                    }

                    fileName = validated.FileName;
                    contentType = validated.ContentType;
                    content = validated.Content;
                }

                db.Attachments.Add(new Attachment
                {
                    EntityType = nameof(LedgerTransaction),
                    EntityId = ledgerTransactionId,
                    FileName = fileName,
                    ContentType = contentType,
                    ByteSize = content.Length,
                    Content = content,
                    CreatedByUserId = userId,
                    CreatedByUserName = userName
                });
            }
            catch (Exception ex)
            {
                errors.Add($"{file.FileName}: {ex.Message}");
            }
        }

        await db.SaveChangesAsync();
        return errors;
    }

    /// <summary>
    /// "Telefondan ekle" ile yakalanan dosyalari ekleyerek kaydeder. Baytlar telefonda
    /// yuklenirken zaten sikistirilmis (ImageAttachmentService) - burada tekrar islenmez.
    /// </summary>
    private async Task SaveCapturedAttachmentsAsync(int ledgerTransactionId, string? captureToken)
    {
        if (string.IsNullOrWhiteSpace(captureToken))
        {
            return;
        }

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var userName = User.FindFirst(ApplicationUserClaimsPrincipalFactory.DisplayNameClaimType)?.Value;
        var files = captureSessions.TakeFiles(captureToken, userId);
        if (files.Count == 0)
        {
            return;
        }

        foreach (var file in files)
        {
            db.Attachments.Add(new Attachment
            {
                EntityType = nameof(LedgerTransaction),
                EntityId = ledgerTransactionId,
                FileName = file.FileName,
                ContentType = file.ContentType,
                ByteSize = file.Content.Length,
                Content = file.Content,
                CreatedByUserId = userId,
                CreatedByUserName = userName
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<List<LedgerAttachmentSummary>> BuildAttachmentSummariesAsync(int ledgerTransactionId)
    {
        return await db.Attachments.AsNoTracking()
            .Where(x => x.EntityType == nameof(LedgerTransaction) && x.EntityId == ledgerTransactionId)
            .OrderBy(x => x.Id)
            .Select(x => new LedgerAttachmentSummary { Id = x.Id, FileName = x.FileName })
            .ToListAsync();
    }

    private async Task<Dictionary<int, List<LedgerAttachmentSummary>>> BuildAttachmentMapAsync(List<int> ledgerIds)
    {
        if (ledgerIds.Count == 0)
        {
            return [];
        }

        var attachments = await db.Attachments.AsNoTracking()
            .Where(x => x.EntityType == nameof(LedgerTransaction) && ledgerIds.Contains(x.EntityId))
            .OrderBy(x => x.Id)
            .Select(x => new { x.EntityId, x.Id, x.FileName })
            .ToListAsync();

        return attachments
            .GroupBy(x => x.EntityId)
            .ToDictionary(g => g.Key, g => g.Select(x => new LedgerAttachmentSummary { Id = x.Id, FileName = x.FileName }).ToList());
    }

    private async Task<Dictionary<int, string>> BuildMahsupUnitMapAsync(List<int> ledgerIds)
    {
        if (ledgerIds.Count == 0)
        {
            return [];
        }

        var rows = await db.MahsupIslemleri.AsNoTracking()
            .Where(x => ledgerIds.Contains(x.LedgerTransactionId))
            .Include(x => x.Unit).ThenInclude(x => x!.Block)
            .Select(x => new { x.LedgerTransactionId, Unit = x.Unit })
            .ToListAsync();

        return rows
            .Where(x => x.Unit is not null)
            .ToDictionary(x => x.LedgerTransactionId, x => x.Unit!.Block is null ? x.Unit.UnitNo : $"{x.Unit.Block.Name}-{x.Unit.UnitNo}");
    }

    private IQueryable<LedgerTransaction> BuildExpenseQuery(IReadOnlyCollection<int> categoryIds, DateTime? startDate, DateTime? endDate)
    {
        return BuildLedgerQuery(CategoryTypeHelper.Gider, categoryIds, startDate, endDate);
    }

    private IQueryable<LedgerTransaction> BuildIncomeQuery(int? categoryId, DateTime? startDate, DateTime? endDate)
    {
        return BuildLedgerQuery(CategoryTypeHelper.Gelir, NormalizeCategoryIds([], categoryId), startDate, endDate);
    }

    private IQueryable<LedgerTransaction> BuildLedgerQuery(string categoryType, IReadOnlyCollection<int> categoryIds, DateTime? startDate, DateTime? endDate)
    {
        var query = db.LedgerTransactions.AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Include(x => x.CashBox)
            .Include(x => x.BankAccount)
            .Where(x => x.IncomeExpenseCategory != null && x.IncomeExpenseCategory.Type == categoryType);

        if (categoryIds.Count > 0)
        {
            query = query.Where(x =>
                x.IncomeExpenseCategoryId.HasValue &&
                categoryIds.Contains(x.IncomeExpenseCategoryId.Value));
        }

        if (startDate.HasValue)
        {
            var start = DateTimeHelper.EnsureUtc(startDate.Value.Date);
            query = query.Where(x => x.Date >= start);
        }

        if (endDate.HasValue)
        {
            var endExclusive = DateTimeHelper.EnsureUtc(endDate.Value.Date.AddDays(1));
            query = query.Where(x => x.Date < endExclusive);
        }

        return query;
    }

    private async Task<List<SelectListItem>> BuildExpenseCategoryOptionsAsync(IReadOnlyCollection<int> selectedIds)
    {
        return await BuildCategoryOptionsAsync(CategoryTypeHelper.Gider, selectedIds);
    }

    private static List<LedgerCategorySummaryRow> BuildCategorySummaryRows(List<LedgerTransaction> rows)
    {
        return rows
            .GroupBy(x => new
            {
                x.IncomeExpenseCategoryId,
                Name = x.IncomeExpenseCategory?.Name ?? "Kategorisiz"
            })
            .Select(x => new LedgerCategorySummaryRow
            {
                CategoryId = x.Key.IncomeExpenseCategoryId,
                CategoryName = x.Key.Name,
                Count = x.Count(),
                TotalAmount = x.Sum(row => row.Amount)
            })
            .OrderByDescending(x => x.TotalAmount)
            .ThenBy(x => x.CategoryName)
            .ToList();
    }

    private async Task<List<SelectListItem>> BuildIncomeCategoryOptionsAsync(int? selectedId)
    {
        return await BuildCategoryOptionsAsync(CategoryTypeHelper.Gelir, NormalizeCategoryIds([], selectedId));
    }

    private async Task<List<SelectListItem>> BuildCategoryOptionsAsync(string categoryType, IReadOnlyCollection<int> selectedIds)
    {
        return await db.IncomeExpenseCategories
            .AsNoTracking()
            .Where(x => x.Active && x.Type == categoryType)
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .Select(x => new SelectListItem($"{CategoryTypeHelper.Display(x.Type)} - {x.Name}", x.Id.ToString(), selectedIds.Contains(x.Id)))
            .ToListAsync();
    }

    private static List<int> NormalizeCategoryIds(IEnumerable<int> categoryIds, int? categoryId)
    {
        var selected = categoryIds
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        if (selected.Count == 0 && categoryId.HasValue && categoryId.Value > 0)
        {
            selected.Add(categoryId.Value);
        }

        return selected;
    }

    private static PaymentChannel ResolvePaymentChannel(LedgerTransactionCreateViewModel model, out int? cashBoxId, out int? bankAccountId)
    {
        if (FinancialAccountHelper.TryParse(model.AccountKey, out var channel, out cashBoxId, out bankAccountId))
        {
            return channel;
        }

        cashBoxId = null;
        bankAccountId = null;
        return model.PaymentChannel;
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
