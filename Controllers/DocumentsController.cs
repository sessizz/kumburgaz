using System.Security.Claims;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[ModuleAuthorize(AppModules.Belgeler)]
public class DocumentsController(
    ApplicationDbContext db,
    DocumentFileService documentFileService,
    CaptureSessionService captureSessions) : Controller
{
    public async Task<IActionResult> Index()
    {
        var rows = await db.DocumentRecords.AsNoTracking()
            .OrderByDescending(x => x.DocumentDate)
            .ThenBy(x => x.Category)
            .ToListAsync();
        return View(rows);
    }

    public IActionResult Create() => View(new DocumentRecord { DocumentDate = DateTime.Today });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DocumentRecord model, List<IFormFile>? files, string? captureToken = null)
    {
        var validatedFiles = await ValidateFilesAsync(files, requireFile: false);
        var capturedFileCount = CapturedFiles(captureToken).Count;
        if (validatedFiles.Count == 0 && capturedFileCount == 0)
        {
            ModelState.AddModelError("files", "En az bir dosya seçin.");
        }

        if (!ModelState.IsValid) return View(model);

        model.DocumentDate = DateTime.SpecifyKind(model.DocumentDate, DateTimeKind.Utc);
        db.DocumentRecords.Add(model);
        await db.SaveChangesAsync();

        db.Attachments.AddRange(validatedFiles.Select(x => documentFileService.CreateAttachment(model.Id, x, User)));
        await db.SaveChangesAsync();
        await SaveCapturedAttachmentsAsync(model.Id, captureToken);
        TempData["ActionSuccess"] = "Belge kaydı oluşturuldu.";
        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var document = await db.DocumentRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (document is null)
        {
            return NotFound();
        }

        var attachments = await BuildAttachmentSummariesAsync(id);
        return View(new DocumentDetailViewModel
        {
            Document = document,
            Attachments = attachments
        });
    }

    public async Task<IActionResult> Edit(int id)
    {
        var entity = await db.DocumentRecords.FindAsync(id);
        return entity is null ? NotFound() : View(entity);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(DocumentRecord model, List<IFormFile>? files, string? captureToken = null)
    {
        var validatedFiles = await ValidateFilesAsync(files, requireFile: false);
        if (!ModelState.IsValid) return View(model);
        var entity = await db.DocumentRecords.FindAsync(model.Id);
        if (entity is null) return NotFound();

        entity.Title = model.Title.Trim();
        entity.Category = model.Category.Trim();
        entity.Note = model.Note;
        entity.DocumentDate = DateTime.SpecifyKind(model.DocumentDate, DateTimeKind.Utc);
        await db.SaveChangesAsync();

        if (validatedFiles.Count > 0)
        {
            db.Attachments.AddRange(validatedFiles.Select(x => documentFileService.CreateAttachment(entity.Id, x, User)));
            await db.SaveChangesAsync();
        }

        await SaveCapturedAttachmentsAsync(entity.Id, captureToken);
        TempData["ActionSuccess"] = "Belge güncellendi.";
        return RedirectToAction(nameof(Details), new { id = entity.Id });
    }

    public async Task<IActionResult> PreviewFile(int documentId, int attachmentId)
    {
        var attachment = await FindDocumentAttachmentAsync(documentId, attachmentId);
        return attachment is null ? NotFound() : File(attachment.Content, attachment.ContentType);
    }

    public async Task<IActionResult> DownloadFile(int documentId, int attachmentId)
    {
        var attachment = await FindDocumentAttachmentAsync(documentId, attachmentId);
        return attachment is null ? NotFound() : File(attachment.Content, attachment.ContentType, attachment.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAttachment(int documentId, int attachmentId)
    {
        var attachment = await db.Attachments.FirstOrDefaultAsync(x =>
            x.Id == attachmentId &&
            x.EntityType == nameof(DocumentRecord) &&
            x.EntityId == documentId);
        if (attachment is null)
        {
            return NotFound();
        }

        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Dosya kaldırıldı.";
        return RedirectToAction(nameof(Edit), new { id = documentId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await db.DocumentRecords.FindAsync(id);
        if (entity is null)
        {
            TempData["ActionError"] = "Belge bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        var attachments = await db.Attachments
            .Where(x => x.EntityType == nameof(DocumentRecord) && x.EntityId == id)
            .ToListAsync();
        db.Attachments.RemoveRange(attachments);
        db.DocumentRecords.Remove(entity);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Belge silindi.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<DocumentFileValidationResult>> ValidateFilesAsync(List<IFormFile>? files, bool requireFile)
    {
        var selectedFiles = (files ?? []).Where(x => x.Length > 0).ToList();
        if (requireFile && selectedFiles.Count == 0)
        {
            ModelState.AddModelError("files", "En az bir dosya seçin.");
        }

        var validatedFiles = new List<DocumentFileValidationResult>();
        foreach (var file in selectedFiles)
        {
            var result = await documentFileService.ValidateAsync(file);
            if (result.IsValid)
            {
                validatedFiles.Add(result);
            }
            else
            {
                ModelState.AddModelError("files", result.ErrorMessage!);
            }
        }

        return validatedFiles;
    }

    private IReadOnlyList<CaptureStagedFile> CapturedFiles(string? captureToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return captureSessions.ListFiles(captureToken, userId);
    }

    /// <summary>
    /// "Telefondan ekle" ile yakalanan dosyalari belge kaydina ekler. Baytlar telefonda
    /// yuklenirken zaten dogrulanmis (DocumentFileService.ValidateAsync) - tekrar okunmaz.
    /// </summary>
    private async Task SaveCapturedAttachmentsAsync(int documentId, string? captureToken)
    {
        var files = CapturedFiles(captureToken);
        if (files.Count == 0)
        {
            return;
        }

        db.Attachments.AddRange(files.Select(x =>
            documentFileService.CreateAttachment(documentId, x.FileName, x.ContentType, x.Content, User)));
        await db.SaveChangesAsync();
        captureSessions.Remove(captureToken);
    }

    private async Task<List<DocumentAttachmentSummary>> BuildAttachmentSummariesAsync(int documentId)
    {
        return await db.Attachments.AsNoTracking()
            .Where(x => x.EntityType == nameof(DocumentRecord) && x.EntityId == documentId)
            .OrderBy(x => x.Id)
            .Select(x => new DocumentAttachmentSummary
            {
                Id = x.Id,
                FileName = x.FileName,
                ContentType = x.ContentType,
                ByteSize = x.ByteSize
            })
            .ToListAsync();
    }

    private Task<Attachment?> FindDocumentAttachmentAsync(int documentId, int attachmentId)
    {
        return db.Attachments.AsNoTracking().FirstOrDefaultAsync(x =>
            x.Id == attachmentId &&
            x.EntityType == nameof(DocumentRecord) &&
            x.EntityId == documentId);
    }
}
