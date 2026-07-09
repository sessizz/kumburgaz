# Document File Uploads Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace URL-only document records with multi-file PostgreSQL attachments, secure download endpoints, and browser previews for common document types.

**Architecture:** `DocumentRecord` remains the metadata owner and generic `Attachment` rows store each file with `EntityType = nameof(DocumentRecord)`. `DocumentFileService` validates and prepares raw uploads; `DocumentsController` owns document-scoped access and file responses. The detail page uses locally served, fixed-version docx-preview and SheetJS assets.

**Tech Stack:** ASP.NET Core MVC, .NET 10, EF Core, PostgreSQL/SQLite, xUnit, docx-preview, SheetJS CE, Razor, vanilla JavaScript.

## Global Constraints

- Continue on `codex/kumburgaz-improvement-plan`; never stage `.codex/`.
- Store all document bytes in `Attachments.Content`, not the application filesystem.
- Permit multiple files per document and retain existing files when new ones are added.
- Limit each file to 25 MiB (`25 * 1024 * 1024` bytes).
- Allow only PDF, JPEG, PNG, WEBP, GIF, DOCX, XLSX, XLS, CSV, and TXT after extension and MIME checks.
- Keep the legacy `DocumentRecord.Url` database column but remove it from all document UI and writes.
- Require `AppModules.Belgeler` authorization for all document/file endpoints.
- Retain original document bytes, including image uploads; do not use `ImageAttachmentService` or apply compression/conversion in this module.
- Write each behavior test before production code and observe the expected failure.

---

### Task 1: Validate raw document files

**Files:**
- Create: `Services/DocumentFileService.cs`
- Create: `tests/Kumburgaz.Web.Tests/DocumentFileServiceTests.cs`
- Modify: `Program.cs`

**Interfaces:**
- Produces `Task<DocumentFileValidationResult> ValidateAsync(IFormFile file, CancellationToken ct = default)`.
- `DocumentFileValidationResult` contains `IsValid`, `ErrorMessage`, `FileName`, `ContentType`, and `Content`.
- Produces `Attachment CreateAttachment(int documentId, DocumentFileValidationResult validated, ClaimsPrincipal user)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async Task ValidateAsync_accepts_a_pdf_and_preserves_its_bytes()
{
    var result = await new DocumentFileService().ValidateAsync(
        CreateFile("yonetim-plani.pdf", "application/pdf", [1, 2, 3]));

    Assert.True(result.IsValid);
    Assert.Equal("yonetim-plani.pdf", result.FileName);
    Assert.Equal("application/pdf", result.ContentType);
    Assert.Equal(new byte[] { 1, 2, 3 }, result.Content);
}

[Fact]
public async Task ValidateAsync_rejects_an_executable_extension()
{
    var result = await new DocumentFileService().ValidateAsync(
        CreateFile("zararli.exe", "application/octet-stream", [1]));

    Assert.False(result.IsValid);
    Assert.Equal("Bu dosya turu desteklenmiyor.", result.ErrorMessage);
}

[Fact]
public async Task ValidateAsync_rejects_a_file_larger_than_25_mib()
{
    var result = await new DocumentFileService().ValidateAsync(
        new FakeFormFile("buyuk.pdf", "application/pdf", 25L * 1024 * 1024 + 1));

    Assert.False(result.IsValid);
    Assert.Equal("Her dosya en fazla 25 MB olabilir.", result.ErrorMessage);
}
```

- [ ] **Step 2: Verify the tests fail for the missing service**

Run: `dotnet test .\tests\Kumburgaz.Web.Tests\Kumburgaz.Web.Tests.csproj --filter FullyQualifiedName~DocumentFileServiceTests --no-restore`

Expected: FAIL because `DocumentFileService` and its result type do not exist.

- [ ] **Step 3: Implement the minimal validator**

```csharp
public sealed class DocumentFileService
{
    public const long MaxFileBytes = 25L * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string[]> AllowedContentTypes =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = ["application/pdf"],
            [".jpg"] = ["image/jpeg"], [".jpeg"] = ["image/jpeg"],
            [".png"] = ["image/png"], [".webp"] = ["image/webp"], [".gif"] = ["image/gif"],
            [".docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
            [".xlsx"] = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
            [".xls"] = ["application/vnd.ms-excel"],
            [".csv"] = ["text/csv", "application/csv"], [".txt"] = ["text/plain"]
        };

    public async Task<DocumentFileValidationResult> ValidateAsync(IFormFile file, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(fileName);
        if (file.Length <= 0) return DocumentFileValidationResult.Invalid("Bos dosya yuklenemez.");
        if (file.Length > MaxFileBytes) return DocumentFileValidationResult.Invalid("Her dosya en fazla 25 MB olabilir.");
        if (!AllowedContentTypes.TryGetValue(extension, out var types) || !types.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            return DocumentFileValidationResult.Invalid("Bu dosya turu desteklenmiyor.");

        await using var input = file.OpenReadStream();
        using var output = new MemoryStream();
        await input.CopyToAsync(output, ct);
        return DocumentFileValidationResult.Valid(fileName, types[0], output.ToArray());
    }
}
```

Add `builder.Services.AddScoped<DocumentFileService>();` beside the existing application service registrations in `Program.cs`.

- [ ] **Step 4: Verify the focused tests pass**

Run: `dotnet test .\tests\Kumburgaz.Web.Tests\Kumburgaz.Web.Tests.csproj --filter FullyQualifiedName~DocumentFileServiceTests --no-restore`

Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```powershell
git add Services/DocumentFileService.cs Program.cs tests/Kumburgaz.Web.Tests/DocumentFileServiceTests.cs
git commit -m "feat: validate document uploads"
```

### Task 2: Store multiple attachments and scope file access to the document

**Files:**
- Modify: `Controllers/DocumentsController.cs`
- Create: `Models/DocumentViewModels.cs`
- Create: `tests/Kumburgaz.Web.Tests/DocumentsControllerTests.cs`

**Interfaces:**
- `Create(DocumentRecord model, List<IFormFile>? files)` and `Edit(DocumentRecord model, List<IFormFile>? files)`.
- `Details(int id)`, `PreviewFile(int documentId, int attachmentId)`, `DownloadFile(int documentId, int attachmentId)`, and `DeleteAttachment(int documentId, int attachmentId)`.
- `DocumentDetailViewModel` exposes `DocumentRecord Document` and `List<DocumentAttachmentSummary> Attachments`; each summary has `Id`, `FileName`, `ContentType`, and `ByteSize`.

- [ ] **Step 1: Write failing persistence and ownership tests**

```csharp
[Fact]
public async Task Create_stores_every_valid_uploaded_file_as_a_document_attachment()
{
    await using var db = CreateDb();
    var result = await CreateController(db).Create(
        new DocumentRecord { Title = "Karar", Category = "Toplanti", DocumentDate = UtcToday() },
        [CreateFile("karar.pdf", "application/pdf", [1]),
         CreateFile("katilim.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [2])]);

    Assert.IsType<RedirectToActionResult>(result);
    var files = await db.Attachments.Where(x => x.EntityType == nameof(DocumentRecord)).OrderBy(x => x.FileName).ToListAsync();
    Assert.Equal(["karar.pdf", "katilim.xlsx"], files.Select(x => x.FileName));
}

[Fact]
public async Task PreviewFile_rejects_an_attachment_owned_by_another_document()
{
    var result = await CreateController(db).PreviewFile(second.Id, attachment.Id);

    Assert.IsType<NotFoundResult>(result);
}

[Fact]
public async Task Delete_removes_all_attachments_owned_by_the_document()
{
    await CreateController(db).Delete(document.Id);

    Assert.Empty(await db.Attachments.IgnoreQueryFilters()
        .Where(x => x.EntityType == nameof(DocumentRecord) && x.EntityId == document.Id).ToListAsync());
}
```

- [ ] **Step 2: Verify the tests fail for missing action signatures**

Run: `dotnet test .\tests\Kumburgaz.Web.Tests\Kumburgaz.Web.Tests.csproj --filter FullyQualifiedName~DocumentsControllerTests --no-restore`

Expected: FAIL because the controller has no file-list parameters, detail action, or document-scoped file endpoints.

- [ ] **Step 3: Implement the controller flow**

```csharp
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Create(DocumentRecord model, List<IFormFile>? files)
{
    var validated = await ValidateFilesAsync(files);
    if (!ModelState.IsValid) return View(model);

    model.DocumentDate = DateTime.SpecifyKind(model.DocumentDate, DateTimeKind.Utc);
    db.DocumentRecords.Add(model);
    await db.SaveChangesAsync();
    db.Attachments.AddRange(validated.Select(x => documentFileService.CreateAttachment(model.Id, x, User)));
    await db.SaveChangesAsync();
    return RedirectToAction(nameof(Details), new { id = model.Id });
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
```

Implement `ValidateFilesAsync` so every invalid selected file adds a model error and no attachment is created. Implement `FindDocumentAttachmentAsync` with exact `EntityType`, `EntityId`, and attachment id filters. `Edit` only adds new attachments and never writes `Url`. `DeleteAttachment` matches both document and attachment. `Delete` removes all matching attachments before removing the document.

- [ ] **Step 4: Verify the controller tests pass**

Run: `dotnet test .\tests\Kumburgaz.Web.Tests\Kumburgaz.Web.Tests.csproj --filter FullyQualifiedName~DocumentsControllerTests --no-restore`

Expected: PASS with create, ownership, download disposition, and delete scenarios.

- [ ] **Step 5: Commit**

```powershell
git add Controllers/DocumentsController.cs Models/DocumentViewModels.cs tests/Kumburgaz.Web.Tests/DocumentsControllerTests.cs
git commit -m "feat: store document files in postgres"
```

### Task 3: Replace the URL UI with document detail and previews

**Files:**
- Modify: `Views/Documents/_DocumentForm.cshtml`
- Modify: `Views/Documents/Index.cshtml`
- Modify: `Views/Documents/Edit.cshtml`
- Create: `Views/Documents/Details.cshtml`
- Create: `wwwroot/js/document-preview.js`
- Create: `wwwroot/css/document-preview.css`
- Create: `wwwroot/lib/docx-preview/docx-preview.min.js`
- Create: `wwwroot/lib/docx-preview/docx-preview.min.css`
- Create: `wwwroot/lib/sheetjs/xlsx.full.min.js`
- Create: `tests/Kumburgaz.Web.Tests/DocumentViewTests.cs`

**Interfaces:**
- `window.DocumentPreview.render({ previewUrl, contentType, fileName, container })` fetches an authenticated preview response and renders it without injecting user-controlled HTML.
- Razor emits `PreviewFile` and `DownloadFile` URLs in data attributes.

- [ ] **Step 1: Write the failing form-contract test**

```csharp
[Fact]
public void Document_form_uses_multipart_multiple_upload_and_has_no_url_input()
{
    var markup = File.ReadAllText(Path.Combine(ProjectRoot, "Views", "Documents", "_DocumentForm.cshtml"));

    Assert.Contains("enctype=\"multipart/form-data\"", markup);
    Assert.Contains("name=\"files\"", markup);
    Assert.Contains("multiple", markup);
    Assert.DoesNotContain("asp-for=\"Url\"", markup);
}
```

- [ ] **Step 2: Verify it fails**

Run: `dotnet test .\tests\Kumburgaz.Web.Tests\Kumburgaz.Web.Tests.csproj --filter FullyQualifiedName~DocumentViewTests --no-restore`

Expected: FAIL because the current form contains the URL input and is not multipart.

- [ ] **Step 3: Implement the Razor, assets, and preview script**

```html
<form method="post" enctype="multipart/form-data" class="space-y-4">
  <input type="file" name="files" multiple
         accept=".pdf,.jpg,.jpeg,.png,.webp,.gif,.docx,.xlsx,.xls,.csv,.txt"
         class="file-input file-input-bordered w-full" />
</form>
```

Replace the list URL column with file count and make the title link to `Details`. The detail page lists every attachment with preview and download controls and supports removing one file from edit. Vendor fixed official release builds of docx-preview and SheetJS under `wwwroot/lib`; load them only in `Details.cshtml`.

In `document-preview.js`, fetch the preview URL as an `ArrayBuffer`. For DOCX call `docx.renderAsync(arrayBuffer, container)`. For XLSX/XLS/CSV call `XLSX.read(arrayBuffer)`, render the selected worksheet using `XLSX.utils.sheet_to_html`, and provide buttons for every sheet. For PDF/images, create a Blob URL and use an iframe/image. Use `textContent` for labels and revoke prior object URLs when replacing the preview. Show a download-only state for other accepted files.

- [ ] **Step 4: Verify the form test passes and the Release build succeeds**

Run: `dotnet test .\tests\Kumburgaz.Web.Tests\Kumburgaz.Web.Tests.csproj --filter FullyQualifiedName~DocumentViewTests --no-restore`

Expected: PASS.

Run: `dotnet build .\Kumburgaz.Web.csproj --configuration Release --no-restore`

Expected: 0 errors.

- [ ] **Step 5: Verify the complete browser flow**

Run: `dotnet run --project .\Kumburgaz.Web.csproj --launch-profile http`

Verify: upload PDF, JPEG, DOCX, XLSX, and CSV; open the document detail; preview each supported type; switch workbook sheets; download each original; add a second file via edit; delete one file; confirm remaining files still work.

- [ ] **Step 6: Commit**

```powershell
git add Views/Documents wwwroot/js/document-preview.js wwwroot/css/document-preview.css wwwroot/lib/docx-preview wwwroot/lib/sheetjs tests/Kumburgaz.Web.Tests/DocumentViewTests.cs
git commit -m "feat: preview uploaded documents"
```

### Task 4: Run full verification and inspect delivery scope

**Files:**
- Verify only; no source changes required.

**Interfaces:**
- Consumes the completed document file service, controller actions, Razor views, and preview assets.
- Produces a beta-ready, focused working tree.

- [ ] **Step 1: Run all tests**

Run: `dotnet test .\kumburgaz.sln --no-restore`

Expected: all tests pass with zero failures.

- [ ] **Step 2: Build Release**

Run: `dotnet build .\Kumburgaz.Web.csproj --configuration Release --no-restore`

Expected: 0 errors.

- [ ] **Step 3: Review scope**

Run: `git diff origin/master...HEAD --check; git status -sb`

Expected: no whitespace errors; `.codex/` remains untracked and unstaged; only beta work is ahead of `origin/master`.

- [ ] **Step 4: Commit a verification correction only if needed**

```powershell
git add Controllers/DocumentsController.cs Models/DocumentViewModels.cs Services/DocumentFileService.cs Views/Documents tests/Kumburgaz.Web.Tests wwwroot/css/document-preview.css wwwroot/js/document-preview.js
git commit -m "fix: finalize document upload flow"
```
