# File Upload Analysis Results: Kumburgaz

## Executive Summary
- Upload sites analyzed: 8
- Vulnerable: 2
- Likely Vulnerable: 0
- Not Vulnerable: 6
- Needs Manual Review: 1

## Findings

### [VULNERABLE] Backup restore — no content validation before overwriting live SQLite DB
- **File**: `Services/BackupService.cs` (lines 60-73, esp. 65-69); `Controllers/BackupsController.cs` (lines 45-82)
- **Endpoint / function**: `POST /Backups/Restore` → `BackupsController.Restore(IFormFile? file)` → `BackupService.RestoreAsync(tempPath)`
- **Issue**: The uploaded file's bytes are trusted with zero content validation (no magic-byte/format check, no extension allowlist, no content-type check at all — `Restore()` only checks `file.Length == 0`). When the active connection string is SQLite, `RestoreAsync` performs `File.Copy(filePath, target, overwrite: true)`, unconditionally replacing the live production database file with whatever bytes were uploaded — even if it is not a valid SQLite database (e.g., garbage, a text file, an empty-ish blob, or a corrupted/malformed SQLite file crafted to exploit a SQLite parser bug).
- **Bypass vector**: Any file, any extension, any content-type (there is no allowlist/blocklist at all on this endpoint — every check present on the Documents upload paths is absent here) is accepted and written straight to disk, then copied over the live DB.
- **Storage path**: `App_Data/RestoreUploads/{guid}-{sanitized filename}` (temp) → copied over the resolved SQLite `Data Source` path (production DB) — file deleted from temp in a `finally` block after use, but the live DB has already been overwritten by that point.
- **Impact**: A SystemAdmin session (legitimate or compromised/CSRF-adjacent, though `[ValidateAntiForgeryToken]` mitigates classic CSRF) can corrupt or destroy the production database by uploading any non-database file — full data-integrity loss / denial of service. A pre-restore backup is taken automatically (`CreateBackupAsync("restore-oncesi")` at line 62) which limits permanent data loss, but the application is immediately left running against a broken/garbage database file until an operator notices and manually restores the backup.
- **Remediation**: Validate the uploaded file is a well-formed SQLite database (check the 16-byte "SQLite format 3\0" header magic, and/or run `PRAGMA integrity_check` against a copy before swapping it into place) or a well-formed pg_restore custom-format dump (check the "PGDMP" magic header) before accepting it. Reject anything else with a clear error, matching the allowlist rigor already applied in `DocumentFileService.ValidateAsync`.
- **Dynamic Test**:
  ```
  curl -X POST https://target/Backups/Restore \
    -H "Cookie: <systemadmin-session-cookie>" \
    -F "__RequestVerificationToken=<csrf-token>" \
    -F "file=@garbage.bin;type=application/octet-stream" 
  # If connection string is SQLite, garbage.bin now overwrites the live database file.
  ```

### [VULNERABLE] Backup restore — pg_restore argument injection via unsanitized filename
- **File**: `Services/BackupService.cs` (lines 72, 129-132, 155-163); `Controllers/BackupsController.cs` (line 57)
- **Endpoint / function**: `POST /Backups/Restore` → `BackupService.RestoreAsync` → `RunProcessAsync(pg_restore, BuildPgRestoreArgs(...))` (Postgres deployments)
- **Issue**: `BuildPgRestoreArgs` builds the process arguments as a single interpolated string: `$"{BuildPgConnectionArgs(connectionString)} --clean --if-exists \"{source}\""`, where `source` is the temp file path containing the attacker-supplied original filename (`Path.GetFileName(file.FileName)` only strips directory separators — it does not strip or escape double-quote characters). `RunProcessAsync` passes this via `new ProcessStartInfo(fileName, arguments)` (the single-string `Arguments` property), not `ArgumentList`. .NET's `Process` class re-parses this single string using Windows-style command-line quoting/splitting rules to build the child process's argv, on all platforms. A `"` character inside the filename therefore closes the quoted `--if-exists "..."` argument early and lets the attacker append arbitrary additional `pg_restore` flags.
- **Bypass vector**: Upload a file with a crafted `Content-Disposition: filename="x\" --data-only\" .dump"` (or similar) in the multipart body. `IFormFile.FileName` is attacker-controlled from the raw multipart request and is not restricted to characters that are legal on the deployment filesystem in general — double quotes are illegal in Windows filenames but legal on Linux/ext4/overlayfs, which is the deployment target implied by the Win32Exception message in `RunProcessAsync` referencing installing `postgresql-client` in a "container". This is not classic OS command injection (no shell is invoked, `UseShellExecute = false`), but it is argument injection against `pg_restore`, letting an attacker append/override flags (e.g., altering restore scope, forcing single-transaction mode off, or other `pg_restore` options) beyond what the application intends to pass.
- **Storage path**: `App_Data/RestoreUploads/{guid}-{filename}` → path string embedded unescaped into the `pg_restore` argument string.
- **Impact**: Combined with SystemAdmin-only access this is a defense-in-depth failure with real leverage: an admin session (or a CSRF-adjacent/compromised admin) can manipulate how `pg_restore` runs against the production database beyond the intended `--clean --if-exists <file>` invocation. Severity is moderated by requiring SystemAdmin auth, but is still worth remediating since it removes a trust boundary the code otherwise appears to assume exists.
- **Remediation**: Use `ProcessStartInfo.ArgumentList` (a true argv list, no re-parsing/quoting ambiguity) instead of building a single `Arguments` string for `pg_dump`/`pg_restore` invocations. Additionally, do not derive the on-disk temp filename from attacker input at all — use `Guid.NewGuid()` alone as the filename (no need to preserve the original name for a restore artifact that is deleted immediately after use).
- **Dynamic Test**:
  ```
  # multipart body with a crafted filename containing an embedded double-quote
  curl -X POST https://target/Backups/Restore \
    -H "Cookie: <systemadmin-session-cookie>" \
    -F "__RequestVerificationToken=<csrf-token>" \
    -F 'file=@dump.dump;filename="x\" --single-transaction=false \"y.dump";type=application/octet-stream'
  # Inspect pg_restore process arguments/logs on the server to confirm the injected flag was applied.
  ```

### [NEEDS MANUAL REVIEW] Document attachment preview — reflected stored Content-Type on inline serving
- **File**: `Controllers/DocumentsController.cs` (lines 90-94, `PreviewFile`)
- **Endpoint / function**: `GET /Documents/PreviewFile?documentId=&attachmentId=` → `return File(attachment.Content, attachment.ContentType);`
- **Uncertainty**: `PreviewFile` serves the stored blob inline (no filename argument, so no `Content-Disposition: attachment` forcing a download, unlike `DownloadFile` at line 96-100 which does supply a filename). The `ContentType` served is the canonical allowlisted value (`contentTypes[0]` from `DocumentFileService`, not raw attacker header), and the extension/content-type allowlist excludes `text/html`/`image/svg+xml`/etc., so classic stored-XSS via a crafted content-type is not directly reachable through this allowlist. However, since there is no magic-byte validation, a file whose extension is `.txt` (allowed, content-type `text/plain`) or `.csv`/`.jpg` etc. could contain attacker-chosen bytes that don't match its claimed type. Whether any browser would sniff/execute such content as HTML when served inline with an explicit (if inaccurate) `Content-Type: text/plain` and no `X-Content-Type-Options: nosniff` response header was not verified — this is a lower-severity, browser-dependent edge case flagged for awareness only.
- **Suggestion**: Confirm whether `nosniff` is set globally (e.g., via security headers middleware) for responses from this action; if not, consider adding it and/or forcing `Content-Disposition: attachment` on `PreviewFile` as well, or restricting inline preview to image/PDF types only.

### [NOT VULNERABLE] Document attachments (Create) — extension bypass via upload
- **File**: `Controllers/DocumentsController.cs` (lines 26-41, 142-165); `Services/DocumentFileService.cs` (lines 27-54)
- **Endpoint / function**: `POST /Documents/Create` (`DocumentsController.Create`)
- **Reason**: `DocumentFileService.ValidateAsync` enforces both an extension allowlist and a matching Content-Type allowlist together (`AllowedContentTypes` dictionary keyed by extension, each mapping to the specific expected MIME type(s); dictionary lookup uses `StringComparer.OrdinalIgnoreCase` so `.PHP`/`.php` case tricks do not bypass it, and the Content-Type comparison also uses `OrdinalIgnoreCase`). Both extension AND content-type must match simultaneously — an attacker cannot satisfy the check with an executable extension (`.php`, `.aspx`, `.html`, etc.) because it is simply not present in the allowlist (allowlist, not blocklist, so there is no bypassable blocklist gap either). `Path.GetFileName` strips any path-traversal components from the filename before use. There is no double-extension logic exploited (`Path.GetExtension` only reads the final extension segment, and the stored filename/content-type used for later serving is the canonical value looked up from the allowlist — `contentTypes[0]` — not the raw attacker-supplied `Content-Type` header, so header spoofing cannot inject an arbitrary content-type into storage). Storage is a DB `byte[]` blob (`Attachment.Content`), not a web-accessible filesystem path, so even an uploaded polyglot file is never directly served as a static/executable resource. No magic-byte/content validation exists, but this only allows an extension/MIME-type-correct file (e.g., a `.txt` renamed with arbitrary text content, or a malformed `.pdf`) — it does not allow smuggling an executable or script type past the allowlist.

### [NOT VULNERABLE] Document attachments (Edit) — extension bypass via upload
- **File**: `Controllers/DocumentsController.cs` (lines 65-88, 142-165)
- **Endpoint / function**: `POST /Documents/Edit` (`DocumentsController.Edit`)
- **Reason**: Identical validation path as Create (shared `ValidateFilesAsync` → `DocumentFileService.ValidateAsync`); see reasoning above.

### [NOT VULNERABLE] Ledger transaction receipt photos (Create)
- **File**: `Controllers/LedgerController.cs` (lines 121-145, `SaveAttachmentsAsync` lines 493-519), `Services/ImageAttachmentService.cs` (lines 20-61)
- **Endpoint / function**: `POST /Ledger/Create` (`LedgerController.Create(LedgerTransactionCreateViewModel model, List<IFormFile> Fotograflar)`)
- **Reason**: Every uploaded file (`file.Length > 0`) is passed to `ImageAttachmentService.CompressAsync`, which: (1) rejects empty files and files > 15MB, (2) calls `Image.LoadAsync(input, ct)` (ImageSharp), which performs magic-byte/format-signature detection and throws `UnknownImageFormatException` (unhandled -> 500) for any non-image content — this is an effective content-based validation, not merely extension/MIME-based, (3) always re-encodes via `AutoOrient().Resize(...)` then `SaveAsJpegAsync` before storage. The stored `Attachment.Content` is always the re-encoded byte array from ImageSharp — the raw uploaded bytes are never persisted. `ContentType` is hardcoded to `"image/jpeg"` (not taken from the request), and `FileName` extension is forced to `.jpg` — original extension/double-extension/path-traversal characters in the filename are discarded, only the base name string is reused for display, never used as a filesystem path. No traversal risk since storage is a DB blob, not a filesystem write. Serving endpoint `LedgerController.Ek(int id)` returns only the re-encoded JPEG bytes with the fixed MIME type, so there is no stored-XSS/RCE risk from this data being served back inline. Decompression-bomb risk is bounded by the 15MB compressed-size cap and is not flagged as a separate finding.

### [NOT VULNERABLE] Ledger transaction receipt photos (Edit)
- **File**: `Controllers/LedgerController.cs` (lines 172-200)
- **Endpoint / function**: `POST /Ledger/Edit` (`LedgerController.Edit(int id, LedgerTransactionCreateViewModel model, List<IFormFile> Fotograflar)`)
- **Reason**: Identical code path — calls the same `SaveAttachmentsAsync` helper which routes every file through `ImageAttachmentService.CompressAsync`. Same mitigations apply: mandatory ImageSharp decode-or-throw, forced re-encode to JPEG, hardcoded `image/jpeg` content type, extension forced to `.jpg`, DB blob storage (no path traversal surface), served back only as re-encoded bytes via `Ek(int id)`.

### [NOT VULNERABLE] Mobile Gider (expense) receipt photos — new entry / mahsup
- **File**: `Areas/Mobile/Controllers/GiderController.cs` (lines 97-180, local `SaveAttachmentsAsync` lines 383-404, `Ek` lines 182-200), `Services/MahsupService.cs` (`CreateAsync` lines 29-118, photo loop 85-99)
- **Endpoint / function**: `POST /m/Gider/Yeni` (`GiderController.Yeni(MobileGiderFormViewModel model, List<IFormFile> Fotograflar)`) — dispatches to `MahsupService.CreateAsync` when `isMahsup` is true, or the controller's own `SaveAttachmentsAsync` otherwise
- **Reason**: Both code paths (mahsup branch and non-mahsup branch) call `imageAttachmentService.CompressAsync(photo)` for every uploaded photo, with no alternate/raw-bytes storage path in either branch. Same mitigating controls as the Ledger sites apply (ImageSharp decode validation, forced JPEG re-encode, hardcoded `image/jpeg` content type, `.jpg`-forced filename, DB blob storage). Additionally, before any photo is accepted, resident (Sakin) users are scope-checked via `scope.CanAccessUnitAsync(User, model.UnitId.Value)` for unit-level authorization — an IDOR-style control, unrelated to file-content validation, confirming no unauthenticated/unauthorized upload path exists. Serving endpoint `GiderController.Ek(int id)` additionally re-checks resident scope via the linked `MahsupIslem.UnitId` before returning the re-encoded JPEG bytes with fixed MIME type.

### [NOT VULNERABLE] Mobile Gider (expense) receipt photos — edit
- **File**: `Areas/Mobile/Controllers/GiderController.cs` (lines 278-329, `SaveAttachmentsAsync` lines 383-405); `Services/ImageAttachmentService.cs` (lines 20-61)
- **Endpoint / function**: `POST /m/Gider/Duzenle/{id}` (`GiderController.Duzenle(int id, MobileGiderFormViewModel model, List<IFormFile> Fotograflar)`)
- **Reason**: Every uploaded file in `Fotograflar` is passed to `imageAttachmentService.CompressAsync(photo)` before being stored — there is no alternate code path that persists raw upload bytes. `CompressAsync` enforces a 15MB size cap, then calls `Image.LoadAsync(input, ct)` which uses ImageSharp's format-sniffing decoders and throws for any non-image payload (e.g. a `.php`/`.aspx`/polyglot file renamed to `.jpg`) — such uploads never reach storage. Valid images are re-oriented, resized, and always re-encoded via `SaveAsJpegAsync`, discarding all original bytes/metadata/extension. The stored `FileName` is forced to `<basename>.jpg` and `ContentType` is hardcoded to `image/jpeg`. Content is stored as a DB blob, not written to a web-servable path, so even a theoretical bypass would not yield direct RCE without a separate file-serving vulnerability.

### [NOT VULNERABLE] CSV import uploads (Units/Collections/Ledger/CashBank)
- **File**: `Services/CsvImportHelper.cs` (lines 8-36); call sites: `Controllers/UnitsController.cs:492`, `Controllers/CollectionsController.cs:289`, `Controllers/LedgerController.cs:256,270`, `Controllers/CashBankController.cs:134`
- **Endpoint / function**: `POST /Units/ImportCsv`, `POST /Collections/ImportCsv`, `POST /Ledger/ImportCsv`, `POST /Ledger/ImportIncomeCsv`, `POST /CashBank/PreviewImport`
- **Reason**: The uploaded `IFormFile` is only ever consumed via `file.OpenReadStream()` wrapped in a `StreamReader`, read line-by-line, and parsed into `string[]` rows held in memory. No file-system write occurs anywhere in this helper, and none of the four controller call sites write the uploaded file to disk before or after calling it. Since the file content is never persisted to a web-accessible (or any) path on the filesystem, there is no file-upload/RCE-execution vector here (no web shell scenario is possible regardless of file extension or content). `CashBankController.PreviewImport` raises `[RequestFormLimits(MultipartBodyLengthLimit = 100MB)]`, widening acceptable upload size but not changing the no-disk-write conclusion.
- **Note (out of scope)**: Parsed CSV values are not sanitized against formula injection before being used downstream. If later exported to Excel/CSV reports opened in a spreadsheet application, this could enable CSV/formula injection against the report viewer. This is a distinct vulnerability class from insecure file upload (already tracked separately per `sast/architecture.md`) and is not deep-dived here.
