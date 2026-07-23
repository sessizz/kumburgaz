# Path Traversal Analysis Results: Kumburgaz

## Executive Summary
- Sinks analyzed: 5 (all in the backup/restore subsystem — `Controllers/BackupsController.cs`, `Services/BackupService.cs`)
- Vulnerable: 0
- Likely Vulnerable: 0
- Not Vulnerable: 5
- Needs Manual Review: 0

No exploitable path traversal was found. Document/image attachments (`DocumentFileService`, `ImageAttachmentService`) store content as in-memory DB `byte[]` blobs with no filesystem I/O, so they present no path traversal surface. No archive extraction (`ZipFile`/`ZipArchive`) exists in the codebase, so ZipSlip is not applicable. CSV/PDF/Excel export helpers stream directly to the HTTP response with no dynamic path construction. The only dynamic file-path sinks in the entire codebase are the backup download/restore endpoints, all of which apply `Path.GetFileName()` (a `basename()`-equivalent sanitizer) before any `Path.Combine()`/file operation, and are additionally gated by `[Authorize(Policy = AppPolicies.SystemAdmin)]` (confirmed as `RequireRole(AppRoles.SistemYonetici)` in `Program.cs`, a genuine restrictive policy).

## Findings

### [NOT VULNERABLE] Backup file download (`BackupsController.Download` → `ResolveBackupPath` → `PhysicalFile`)
- **File**: `Controllers/BackupsController.cs` (lines 39-43), `Services/BackupService.cs` (lines 75-85)
- **Endpoint / function**: `GET /Backups/Download?fileName=...` → `BackupsController.Download(string fileName)` → `BackupService.ResolveBackupPath(fileName)`
- **Reason**: `fileName` is a query-string-bound action parameter, fully user-controlled, and flows into `Path.Combine(BackupDirectory, safeFile)`. However `ResolveBackupPath` first computes `safeFile = Path.GetFileName(fileName)`, which strips everything up to and including the last directory separator, neutralizing `../`, absolute paths, and drive-letter prefixes. Deployment target is confirmed (via Dockerfile, `mcr.microsoft.com/dotnet/aspnet:10.0`) to be Linux, where `/` is the only recognized separator; backslash payloads (`..\..\etc\passwd`) pass through as a single literal filename (Linux allows `\` as an ordinary filename character) and fail `File.Exists`. The one edge case where `Path.GetFileName` leaves input unchanged — a bare `..` with no separator — resolves to the parent directory of `BackupDirectory`, but `File.Exists()` returns `false` for directories, so `ResolveBackupPath` throws `FileNotFoundException` before `PhysicalFile` is ever reached. Endpoint is additionally gated by class-level `[Authorize(Policy = AppPolicies.SystemAdmin)]`.
- **Taint trace**: HTTP query string `fileName` → `Download(string fileName)` param → `ResolveBackupPath(fileName)` → `Path.GetFileName(fileName)` (sanitizer) → `Path.Combine(BackupDirectory, safeFile)` → `File.Exists` gate → `PhysicalFile(path, ...)`.
- **Mitigation confirmed effective**: `Path.GetFileName()` applied before `Path.Combine()`, correctly collapsing any payload to at most one path segment relative to `BackupDirectory`; the sole surviving edge case (bare `..`) is blocked by the subsequent `File.Exists` directory-vs-file check.
- **Optional hardening** (defense-in-depth, not required): add an explicit `Path.GetFullPath(fullPath)` prefix check against `Path.GetFullPath(BackupDirectory)` to make the guarantee robust independent of `File.Exists` semantics or future ports to other operating systems.

### [NOT VULNERABLE] Backup restore upload — temp file write (`BackupsController.Restore`, write path)
- **File**: `Controllers/BackupsController.cs` (lines 47-64)
- **Endpoint / function**: `POST /Backups/Restore` (`[ValidateAntiForgeryToken]`, class-level `[Authorize(Policy = AppPolicies.SystemAdmin)]`) → `BackupsController.Restore(IFormFile? file)`
- **Reason**: `file.FileName` (multipart `Content-Disposition: filename="..."` header) is attacker-controlled, but is sanitized via `Path.GetFileName(file.FileName)` and used only as a suffix appended after a fresh `Guid.NewGuid():N-` prefix: `Path.Combine(tempDir, $"{Guid.NewGuid():N}-{Path.GetFileName(file.FileName)}")`. Because the sanitized value is never the sole/first `Path.Combine` argument, even pathological `GetFileName` edge cases stay confined to the fixed `App_Data/RestoreUploads` directory.
- **Taint trace**: Multipart `file.FileName` header → `Restore(IFormFile? file)` param → `Path.GetFileName(file.FileName)` (sanitized) → GUID-prefixed suffix → `Path.Combine(tempDir, ...)` → `System.IO.File.Create(tempPath)`.
- **Mitigation confirmed effective**: `Path.GetFileName()` sanitization plus GUID-prefixing within a fixed base directory.

### [NOT VULNERABLE] Backup restore — DB restore from temp path (`BackupService.RestoreAsync`)
- **File**: `Controllers/BackupsController.cs` (line 66), `Services/BackupService.cs` (lines 60-73)
- **Endpoint / function**: `BackupsController.Restore` → `BackupService.RestoreAsync(tempPath)` → `File.Copy(filePath, target, overwrite: true)` (SQLite) or `pg_restore` process argument (Postgres, via `RunProcessAsync`/`BuildPgRestoreArgs`)
- **Reason**: `filePath` is not attacker-supplied at this stage — it is the already-sanitized, server-generated `tempPath` produced by the upload sink above, confined to the fixed `RestoreUploads` directory. `target` (SQLite branch) is derived from the server-side `DefaultConnection` connection string, not user input.
- **Taint trace**: Server-generated `tempPath` (from upload sink) → `RestoreAsync(filePath)` param → `File.Copy` source / `pg_restore` argument.
- **Note (out of scope for path traversal)**: `BuildPgRestoreArgs`/`RunProcessAsync` build a raw argument string passed to `Process.Start`, which is a command-injection-adjacent concern properly covered by the RCE check, not path traversal, since `filePath`'s directory is fixed and its basename cannot contain separators.

### [NOT VULNERABLE] Backup path resolution filename sanitization boundary (`ResolveBackupPath`)
- **File**: `Services/BackupService.cs` (lines 75-85)
- **Endpoint / function**: Same as the `Download` finding above — this is the shared sanitization boundary reused by that endpoint.
- **Reason**: Duplicate analysis point of the `Download` finding — `Path.GetFileName(fileName)` applied before `Path.Combine(BackupDirectory, safeFile)` is an effective, correctly-ordered mitigation on the confirmed Linux deployment target. See detailed payload analysis in the first finding above (`/`, `\`, absolute path, and bare `..` cases all fail to escape `BackupDirectory`).

### [NOT VULNERABLE] Backup directory listing (`BackupService.ListBackups`)
- **File**: `Services/BackupService.cs` (lines 24-37)
- **Endpoint / function**: `BackupsController.Index()` (no parameters) → `BackupService.ListBackups()` → `Directory.GetFiles(BackupDirectory)`
- **Reason**: `BackupDirectory` is derived entirely from server-side configuration (`configuration["Backups:Directory"]`) with a hardcoded fallback (`ContentRootPath/App_Data/Backups`). No request/user-supplied value influences this path, and `Index()` takes no route/query parameters.
