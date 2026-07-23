using System.Security.Claims;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Areas.Mobile.Controllers;

[Area("Mobile")]
[ModuleAuthorize(AppModules.Muhasebe)]
public class GiderController(
    ApplicationDbContext db,
    MobileScopeService scope,
    MahsupService mahsupService,
    ImageAttachmentService imageAttachmentService,
    PermissionService permissionService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var isResident = scope.IsResident(User);

        if (isResident)
        {
            var allowedUnitIds = (await scope.GetAllowedUnitIdsAsync(User) ?? []).ToList();

            var mahsuplar = await db.MahsupIslemleri.AsNoTracking()
                .Include(x => x.LedgerTransaction).ThenInclude(x => x!.IncomeExpenseCategory)
                .Include(x => x.Unit).ThenInclude(x => x!.Block)
                .Where(x => allowedUnitIds.Contains(x.UnitId))
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            var attMap = await BuildFirstAttachmentMapAsync(mahsuplar.Select(x => x.LedgerTransactionId));

            var items = mahsuplar.Select(m => new MobileGiderListItem
            {
                Id = m.LedgerTransactionId,
                Date = m.LedgerTransaction!.Date,
                CategoryName = m.LedgerTransaction.IncomeExpenseCategory?.Name ?? "Gider",
                Amount = m.LedgerTransaction.Amount,
                Description = m.LedgerTransaction.Description,
                HasAttachment = attMap.ContainsKey(m.LedgerTransactionId),
                AttachmentId = attMap.GetValueOrDefault(m.LedgerTransactionId),
                IsMahsup = true,
                MahsupId = m.Id,
                UnitDisplay = UnitLabel(m.Unit)
            }).ToList();

            ViewBag.IsResident = true;
            return View(items);
        }

        var rows = await db.LedgerTransactions.AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Where(x => !x.IsTransfer && x.IncomeExpenseCategory != null && x.IncomeExpenseCategory.Type == CategoryTypeHelper.Gider)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .Take(50)
            .ToListAsync();

        var ledgerIds = rows.Select(x => x.Id).ToList();
        var attachmentMap = await BuildFirstAttachmentMapAsync(ledgerIds);
        var mahsupMap = await db.MahsupIslemleri.AsNoTracking()
            .Where(x => ledgerIds.Contains(x.LedgerTransactionId))
            .Include(x => x.Unit).ThenInclude(x => x!.Block)
            .ToDictionaryAsync(x => x.LedgerTransactionId);

        var listItems = rows.Select(x =>
        {
            mahsupMap.TryGetValue(x.Id, out var mahsup);
            return new MobileGiderListItem
            {
                Id = x.Id,
                Date = x.Date,
                CategoryName = x.IncomeExpenseCategory?.Name ?? "Gider",
                Amount = x.Amount,
                Description = x.Description,
                HasAttachment = attachmentMap.ContainsKey(x.Id),
                AttachmentId = attachmentMap.GetValueOrDefault(x.Id),
                IsMahsup = mahsup is not null,
                MahsupId = mahsup?.Id,
                UnitDisplay = mahsup is null ? null : UnitLabel(mahsup.Unit)
            };
        }).ToList();

        ViewBag.IsResident = false;
        return View(listItems);
    }

    [HttpGet]
    public async Task<IActionResult> Yeni()
    {
        return View(await BuildFormAsync(new MobileGiderFormViewModel()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Yeni(MobileGiderFormViewModel model, List<IFormFile> Fotograflar)
    {
        var isResident = scope.IsResident(User);
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userName = User.FindFirst(ApplicationUserClaimsPrincipalFactory.DisplayNameClaimType)?.Value;
        var photos = Fotograflar.Where(f => f.Length > 0).ToList();

        if (!model.Amount.HasValue || model.Amount <= 0)
        {
            ModelState.AddModelError(nameof(model.Amount), "Geçerli bir tutar giriniz.");
        }

        if (!model.CategoryId.HasValue)
        {
            ModelState.AddModelError(nameof(model.CategoryId), "Kategori seçiniz.");
        }

        var isMahsup = isResident || model.IsMahsup;

        if (isMahsup)
        {
            if (!model.UnitId.HasValue)
            {
                ModelState.AddModelError(nameof(model.UnitId), "Daire seçiniz.");
            }
            else if (!await scope.CanAccessUnitAsync(User, model.UnitId.Value))
            {
                ModelState.AddModelError(nameof(model.UnitId), "Bu daire için yetkiniz yok.");
            }

            if (isResident && photos.Count == 0)
            {
                ModelState.AddModelError(nameof(Fotograflar), "Fiş/fatura fotoğrafı zorunludur.");
            }
        }

        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(model));
        }

        try
        {
            if (isMahsup)
            {
                await mahsupService.CreateAsync(new MahsupService.MahsupCreateRequest(
                    model.UnitId!.Value,
                    model.CategoryId!.Value,
                    model.Amount!.Value,
                    model.Description,
                    photos,
                    userId,
                    userName));
                TempData["MobileSuccess"] = "Mahsuplu gider kaydedildi.";
            }
            else
            {
                var cashBox = await db.CashBoxes.Where(x => x.Active).OrderBy(x => x.Id).FirstOrDefaultAsync();
                var ledger = new LedgerTransaction
                {
                    Date = DateTimeHelper.EnsureUtc(DateTime.UtcNow.Date),
                    IncomeExpenseCategoryId = model.CategoryId!.Value,
                    Amount = model.Amount!.Value,
                    PaymentChannel = PaymentChannel.Cash,
                    CashBoxId = cashBox?.Id,
                    Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim()
                };
                db.LedgerTransactions.Add(ledger);
                await db.SaveChangesAsync();

                await SaveAttachmentsAsync(ledger.Id, photos, userId, userName);
                TempData["MobileSuccess"] = "Gider kaydedildi.";
            }
        }
        catch (MahsupService.MahsupValidationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(await BuildFormAsync(model));
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Ek(int id)
    {
        var attachment = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (attachment is null)
        {
            return NotFound();
        }

        if (scope.IsResident(User))
        {
            var mahsup = await db.MahsupIslemleri.AsNoTracking()
                .FirstOrDefaultAsync(x => x.LedgerTransactionId == attachment.EntityId);
            if (mahsup is null || !await scope.CanAccessUnitAsync(User, mahsup.UnitId))
            {
                return NotFound();
            }
        }

        return File(attachment.Content, attachment.ContentType, attachment.FileName);
    }

    public async Task<IActionResult> Detay(int id)
    {
        var ledger = await db.LedgerTransactions.AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Include(x => x.CashBox)
            .Include(x => x.BankAccount)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsTransfer);
        if (ledger is null)
        {
            return NotFound();
        }

        var mahsup = await db.MahsupIslemleri.AsNoTracking()
            .Include(x => x.Unit).ThenInclude(x => x!.Block)
            .FirstOrDefaultAsync(x => x.LedgerTransactionId == id);

        var isResident = scope.IsResident(User);
        if (isResident && (mahsup is null || !await scope.CanAccessUnitAsync(User, mahsup.UnitId)))
        {
            // Sakin yalnizca kendi mahsubunu gorebilir.
            return NotFound();
        }

        var canWrite = permissionService.CanWrite(User, AppModules.Muhasebe);
        var attachments = await db.Attachments.AsNoTracking()
            .Where(x => x.EntityType == nameof(LedgerTransaction) && x.EntityId == id)
            .OrderBy(x => x.Id)
            .Select(x => new MobileAttachmentSummary { Id = x.Id, FileName = x.FileName })
            .ToListAsync();

        return View(new MobileGiderDetailViewModel
        {
            Id = ledger.Id,
            Date = ledger.Date,
            CategoryName = ledger.IncomeExpenseCategory?.Name ?? "Gider",
            Amount = ledger.Amount,
            Description = ledger.Description,
            AccountLabel = ledger.CashBox?.Name ?? ledger.BankAccount?.Name,
            IsMahsup = mahsup is not null,
            MahsupId = mahsup?.Id,
            UnitDisplay = UnitLabel(mahsup?.Unit),
            Attachments = attachments,
            CanEdit = canWrite || isResident,   // Sakin buraya ancak kendi mahsubu icin ulasabildi
            // Sakin'in Muhasebe write yetkisi yalnizca mahsup OLUSTURMAK icindir; silme her zaman personel/yonetici isidir.
            CanDeleteMahsup = canWrite && !isResident && mahsup is not null
        });
    }

    [HttpGet]
    public async Task<IActionResult> Duzenle(int id)
    {
        var (ledger, mahsup, error) = await LoadEditableAsync(id);
        if (error is not null)
        {
            return error;
        }

        var model = new MobileGiderFormViewModel
        {
            Id = ledger!.Id,
            IsEdit = true,
            CategoryId = ledger.IncomeExpenseCategoryId,
            Amount = ledger.Amount,
            Description = ledger.Description,
            IsMahsup = mahsup is not null,
            UnitId = mahsup?.UnitId,
            UnitDisplay = UnitLabel(mahsup?.Unit),
            AmountCategoryEditable = mahsup is null,
            CanRemoveAttachments = !scope.IsResident(User),
            ExistingAttachments = await BuildAttachmentSummariesAsync(id)
        };

        return View(await BuildFormAsync(model));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Duzenle(int id, MobileGiderFormViewModel model, List<IFormFile> Fotograflar)
    {
        var (ledger, mahsup, error) = await LoadEditableAsync(id);
        if (error is not null)
        {
            return error;
        }

        var amountCategoryEditable = mahsup is null;
        if (amountCategoryEditable)
        {
            if (!model.Amount.HasValue || model.Amount <= 0)
            {
                ModelState.AddModelError(nameof(model.Amount), "Geçerli bir tutar giriniz.");
            }

            if (!model.CategoryId.HasValue)
            {
                ModelState.AddModelError(nameof(model.CategoryId), "Kategori seçiniz.");
            }
        }

        if (!ModelState.IsValid)
        {
            model.Id = id;
            model.IsEdit = true;
            model.IsMahsup = mahsup is not null;
            model.UnitDisplay = UnitLabel(mahsup?.Unit);
            model.AmountCategoryEditable = amountCategoryEditable;
            model.CanRemoveAttachments = !scope.IsResident(User);
            model.ExistingAttachments = await BuildAttachmentSummariesAsync(id);
            return View(await BuildFormAsync(model));
        }

        ledger!.Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description.Trim();
        if (amountCategoryEditable)
        {
            ledger.IncomeExpenseCategoryId = model.CategoryId;
            ledger.Amount = model.Amount!.Value;
        }

        await db.SaveChangesAsync();

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userName = User.FindFirst(ApplicationUserClaimsPrincipalFactory.DisplayNameClaimType)?.Value;
        await SaveAttachmentsAsync(id, Fotograflar.Where(f => f.Length > 0).ToList(), userId, userName);

        TempData["MobileSuccess"] = "Gider güncellendi.";
        return RedirectToAction(nameof(Detay), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EkSil(int attachmentId, int ledgerTransactionId)
    {
        // Fotograf kaldirma yalnizca personel/yonetici icin; Sakin kanit fotografini silemez.
        if (scope.IsResident(User))
        {
            return Forbid();
        }

        var attachment = await db.Attachments
            .FirstOrDefaultAsync(x => x.Id == attachmentId && x.EntityType == nameof(LedgerTransaction) && x.EntityId == ledgerTransactionId);
        if (attachment is not null)
        {
            db.Attachments.Remove(attachment);
            await db.SaveChangesAsync();
            TempData["MobileSuccess"] = "Fotoğraf kaldırıldı.";
        }

        return RedirectToAction(nameof(Duzenle), new { id = ledgerTransactionId });
    }

    // Duzenleme icin yetki/kapsam kontrolu: (ledger, mahsup, hataYaniti). Hata varsa ledger/mahsup null olabilir.
    private async Task<(LedgerTransaction? Ledger, MahsupIslem? Mahsup, IActionResult? Error)> LoadEditableAsync(int id)
    {
        var ledger = await db.LedgerTransactions.FirstOrDefaultAsync(x => x.Id == id && !x.IsTransfer);
        if (ledger is null)
        {
            return (null, null, NotFound());
        }

        var mahsup = await db.MahsupIslemleri.AsNoTracking()
            .Include(x => x.Unit).ThenInclude(x => x!.Block)
            .FirstOrDefaultAsync(x => x.LedgerTransactionId == id);

        var isResident = scope.IsResident(User);
        if (isResident)
        {
            // Sakin yalnizca kendi mahsubunu duzenleyebilir (tutar/kategori haric).
            if (mahsup is null || !await scope.CanAccessUnitAsync(User, mahsup.UnitId))
            {
                return (null, null, NotFound());
            }
        }
        else if (!permissionService.CanWrite(User, AppModules.Muhasebe))
        {
            return (null, null, Forbid());
        }

        return (ledger, mahsup, null);
    }

    private async Task SaveAttachmentsAsync(int ledgerTransactionId, List<IFormFile> photos, string? userId, string? userName)
    {
        foreach (var photo in photos)
        {
            var compressed = await imageAttachmentService.CompressAsync(photo);
            db.Attachments.Add(new Attachment
            {
                EntityType = nameof(LedgerTransaction),
                EntityId = ledgerTransactionId,
                FileName = compressed.FileName,
                ContentType = compressed.ContentType,
                ByteSize = compressed.Content.Length,
                Content = compressed.Content,
                CreatedByUserId = userId,
                CreatedByUserName = userName
            });
        }

        if (photos.Count > 0)
        {
            await db.SaveChangesAsync();
        }
    }

    private async Task<List<MobileAttachmentSummary>> BuildAttachmentSummariesAsync(int ledgerTransactionId)
    {
        return await db.Attachments.AsNoTracking()
            .Where(x => x.EntityType == nameof(LedgerTransaction) && x.EntityId == ledgerTransactionId)
            .OrderBy(x => x.Id)
            .Select(x => new MobileAttachmentSummary { Id = x.Id, FileName = x.FileName })
            .ToListAsync();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MahsupSil(int mahsupId)
    {
        // Sakin kendi mahsup kaydini silemez; yanlislik varsa talep acar.
        if (scope.IsResident(User))
        {
            return Forbid();
        }

        var ok = await mahsupService.DeleteAsync(mahsupId);
        TempData[ok ? "MobileSuccess" : "MobileError"] = ok ? "Mahsup silindi." : "Mahsup bulunamadı.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<MobileGiderFormViewModel> BuildFormAsync(MobileGiderFormViewModel model)
    {
        var isResident = scope.IsResident(User);
        model.IsResident = isResident;

        var allowedUnitIds = await scope.GetAllowedUnitIdsAsync(User);
        var unitsQuery = db.Units.AsNoTracking().Include(x => x.Block).Where(x => x.Active);
        if (allowedUnitIds is not null)
        {
            unitsQuery = unitsQuery.Where(x => allowedUnitIds.Contains(x.Id));
        }

        model.UnitOptions = await unitsQuery
            .OrderBy(x => x.Block!.Name).ThenBy(x => x.UnitNo)
            .Select(x => new SelectListItem($"{x.Block!.Name}-{x.UnitNo}", x.Id.ToString(), model.UnitId == x.Id))
            .ToListAsync();

        model.CategoryOptions = await db.IncomeExpenseCategories.AsNoTracking()
            .Where(x => x.Active && x.Type == CategoryTypeHelper.Gider)
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString(), model.CategoryId == x.Id))
            .ToListAsync();

        return model;
    }

    private async Task<Dictionary<int, int>> BuildFirstAttachmentMapAsync(IEnumerable<int> ledgerIds)
    {
        var ids = ledgerIds.ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Attachments.AsNoTracking()
            .Where(x => x.EntityType == nameof(LedgerTransaction) && ids.Contains(x.EntityId))
            .GroupBy(x => x.EntityId)
            .Select(g => new { EntityId = g.Key, Id = g.Min(a => a.Id) })
            .ToDictionaryAsync(x => x.EntityId, x => x.Id);
    }

    private static string? UnitLabel(Unit? unit)
        => unit is null ? null : (unit.Block is null ? unit.UnitNo : $"{unit.Block.Name}-{unit.UnitNo}");
}
