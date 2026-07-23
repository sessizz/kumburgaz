# IDOR Analysis Results: Kumburgaz

## Executive Summary
- Candidates analyzed: 6
- Vulnerable: 3
- Likely Vulnerable: 0
- Not Vulnerable: 3
- Needs Manual Review: 0

## Findings

### [VULNERABLE] Mobile Gider attachment download by id — missing EntityType filter allows cross-entity object confusion
- **File**: `Areas/Mobile/Controllers/GiderController.cs` (lines 182-201)
- **Endpoint**: `GET /m/Gider/Ek?id={attachmentId}`
- **Issue**: `Attachments` are a shared table keyed by `(EntityType, EntityId)` — the same `Attachment.Id` integer space is used for `LedgerTransaction` mahsup receipts, `DocumentRecord` files (see `Controllers/DocumentsController.cs`, `Services/DocumentFileService.cs`), and any other entity that attaches files. The `Ek` action looks the attachment up **only by `Id`**:
  ```csharp
  var attachment = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
  ```
  It never checks `attachment.EntityType == nameof(LedgerTransaction)`. Every sibling method in the *same file* (`Detay` line 228, `EkSil` line 342, `BuildAttachmentSummariesAsync` line 410, `BuildFirstAttachmentMapAsync` line 466) — and every attachment query in `Controllers/LedgerController.cs` (219, 526, 540) and `Controllers/DocumentsController.cs` (108, 133, 170, 186) — filters on `EntityType` in addition to `EntityId`. `Ek` is the outlier.

  The subsequent resident-scoping check then does:
  ```csharp
  var mahsup = await db.MahsupIslemleri.AsNoTracking()
      .FirstOrDefaultAsync(x => x.LedgerTransactionId == attachment.EntityId);
  if (mahsup is null || !await scope.CanAccessUnitAsync(User, mahsup.UnitId)) { return NotFound(); }
  ```
  This treats `attachment.EntityId` as if it were always a `LedgerTransactionId`, but for any attachment whose real `EntityType` is `"DocumentRecord"` (or any future entity type), `EntityId` is actually a `DocumentRecord.Id` (or other primary key) — a value drawn from a completely different auto-increment sequence that can numerically coincide with a `LedgerTransactionId` the resident legitimately has a mahsup for.
- **Impact**: A Sakin (resident) can retrieve the raw file content (`attachment.Content`/`ContentType`/`FileName`) of an **unrelated Attachment row belonging to a different entity type** — e.g. a staff-only `DocumentRecord` file that the resident has no module permission to view at all (Belgeler module is not granted to Sakin) — by brute-forcing `Attachment.Id` values and finding one whose `EntityId` happens to equal a `LedgerTransactionId` from one of the resident's own accessible mahsup entries. Because both `Attachment.Id` and every other entity's PK are small sequential integers in this single-tenant app, and a resident can already enumerate their own valid `LedgerTransactionId` values (exposed via their own `/m/Gider` list `Id` field, which equals `MahsupIslem.LedgerTransactionId`), the search space to find a colliding `EntityId` is small and practically discoverable by iterating `id=1,2,3,...` and inspecting which requests return `200 OK` with unexpected file content/filename instead of the expected mahsup receipt.
- **Proof**: Code path traced above — confirmed `Ek` (line 184) omits the `EntityType == nameof(LedgerTransaction)` predicate that all analogous lookups in this codebase apply, and `Attachment.EntityType` exists precisely to disambiguate cross-entity id collisions (`Models/DomainModels.cs:606`, index at `Data/ApplicationDbContext.cs:273`).
- **Remediation**: Add the missing type filter, matching the pattern used everywhere else in the file:
  ```csharp
  var attachment = await db.Attachments.AsNoTracking()
      .FirstOrDefaultAsync(x => x.Id == id && x.EntityType == nameof(LedgerTransaction));
  ```
- **Dynamic Test**:
  ```
  # As a Sakin (resident) mobile user with a valid session cookie for account+PIN login:
  # 1. Note a LedgerTransactionId you legitimately own via GET /m/Gider (list Id values), e.g. id=42.
  # 2. Brute-force attachment ids to find one whose EntityId coincides with 42 but is NOT
  #    actually a LedgerTransaction attachment (e.g. a DocumentRecord attachment):
  for i in $(seq 1 500); do
    curl -s -o /dev/null -w "%{http_code} id=$i\n" \
      -H "Cookie: <SAKIN_SESSION_COOKIE>" \
      "https://<host>/m/Gider/Ek?id=$i"
  done
  # 3. For any id returning 200, inspect Content-Disposition/filename — if it does not match a
  #    known mahsup receipt filename (e.g. it's a staff document like a contract or minutes PDF),
  #    the EntityType check is confirmed missing and cross-entity disclosure is exploitable.
  ```

### [VULNERABLE] Mobile push-subscription delete by endpoint (no ownership check)
- **File**: `Areas/Mobile/Controllers/BildirimlerController.cs` (lines 77-92)
- **Endpoint**: `POST /m/Bildirimler/AbonelikSil`
- **Issue**: The action looks up and deletes a `PushSubscription` solely by `x.Endpoint == endpoint`, a value fully controlled by the caller via form data. It never compares `existing.UserId` to the current authenticated user's id (`CurrentUserId`, derived from `ClaimTypes.NameIdentifier` — available in this controller but simply not used here, unlike `Abone`, `Ac`, `Ozet`, `TumunuOku` in the same file which all correctly load and check `CurrentUserId`). Any authenticated user — including a low-privilege "Sakin" resident account, since this endpoint lives in `Areas/Mobile` which Sakin accounts are allowed to reach — can delete any other user's push subscription row by supplying that user's endpoint value.
- **Impact**: Cross-user deletion of another user's Web Push subscription record, silently disabling their push notifications until they re-subscribe. Low direct confidentiality impact (no data returned) but a clear availability/integrity violation of another user's resource, and it establishes that this endpoint has zero authorization/ownership enforcement (contrast with the sibling `Abone` action which at least ties the write to `CurrentUserId`, albeit insufficiently — see finding below).
- **Proof**: `db.PushSubscriptions.FirstOrDefaultAsync(x => x.Endpoint == endpoint)` is the only filter; the fetched `existing` object is removed without any check against `CurrentUserId`/`existing.UserId`. `CurrentUserId` is defined on the controller (line 17) but not referenced anywhere in this action.
- **Exploitability caveat**: The `endpoint` is a Web Push subscription URL (e.g. `https://fcm.googleapis.com/fcm/send/<long-opaque-token>` or similar for other push services) generated client-side by the browser's push service and normally not observable by other users or guessable by brute force. Practical exploitation requires the attacker to already know/leak a specific victim's endpoint value (e.g. via another vulnerability, shared device, log exposure, or the reassignment bug in the finding below, which could be chained to first learn/overwrite an endpoint's ownership). The code-level authorization gap is real and should be fixed regardless of current exploit difficulty.
- **Remediation**: Load `CurrentUserId` at the top of the action (as done in the other actions in this controller) and add `&& x.UserId == userId` to the `FirstOrDefaultAsync` predicate (or check `existing.UserId == userId` after fetch before removing), returning `NotFound()`/`Forbid()` when it doesn't match.
- **Dynamic Test**:
  ```
  # As User B (or Sakin account), first learn/observe User A's push endpoint value
  # (e.g. from a prior request/response, shared browser profile, or via the reassignment bug below).

  curl -X POST https://<host>/m/Bildirimler/AbonelikSil \
    -H "Cookie: <USER_B_SESSION_COOKIE>" \
    -H "RequestVerificationToken: <USER_B_CSRF_TOKEN>" \
    --data-urlencode "endpoint=<USER_A_PUSH_ENDPOINT_URL>"

  # Expect: HTTP 200 Ok(), and User A's PushSubscription row is deleted from the DB
  # even though the caller is User B, not User A.
  ```

### [VULNERABLE] Mobile push-subscription create/update by endpoint (no ownership check, subscription hijack)
- **File**: `Areas/Mobile/Controllers/BildirimlerController.cs` (lines 40-74)
- **Endpoint**: `POST /m/Bildirimler/Abone`
- **Issue**: When an `endpoint` value already exists in `PushSubscriptions` (possibly belonging to a different user), the action unconditionally reassigns `existing.UserId = userId` (the caller's own id) and overwrites `P256dh`/`Auth`/`UserAgent`, without first checking whether `existing.UserId` already equals the caller. There is no ownership verification before the update branch is taken — the only gate is that `endpoint`/`p256dh`/`auth` are non-empty strings.
- **Impact**: If an attacker learns or otherwise causes an endpoint collision with a victim's existing subscription row, they can reassign that subscription's ownership to themselves and overwrite its `P256dh`/`Auth` encryption keys, effectively hijacking the victim's push channel (victim stops receiving pushes; attacker's client is now recorded as the owner of that DB row, though actual push delivery still targets the browser tied to the endpoint URL — so the direct payoff is disruption/UserId-record corruption more than a full notification hijack). This also means the row's `UserId` foreign key is fully attacker-influenced given knowledge of the endpoint value alone.
- **Proof**: `existing = FirstOrDefaultAsync(x => x.Endpoint == endpoint)` followed by unconditional `existing.UserId = userId` in the `else` branch, with no prior check of `existing.UserId == userId`.
- **Exploitability caveat**: Same as the finding above — `endpoint` is an opaque, browser-generated Web Push URL that is not normally observable or guessable across users, which significantly limits practical exploitability in isolation. Realistic attack requires an information leak of another user's endpoint value first (log access, shared device, XSS elsewhere, etc.).
- **Remediation**: Before overwriting an existing row, check `existing.UserId == userId`; if not, either reject the request (return `Forbid()`/`BadRequest()`) or create a new row for the caller instead of stealing the existing one. Consider also making `Endpoint` scoped/unique per `(UserId, Endpoint)` rather than globally unique, so a collision on endpoint alone can't cross user boundaries.
- **Dynamic Test**:
  ```
  # Precondition: User A has already subscribed, creating a PushSubscriptions row
  # with Endpoint=<SHARED_ENDPOINT>, UserId=<USER_A_ID>.

  # As User B, replay the same endpoint value:
  curl -X POST https://<host>/m/Bildirimler/Abone \
    -H "Cookie: <USER_B_SESSION_COOKIE>" \
    -H "RequestVerificationToken: <USER_B_CSRF_TOKEN>" \
    --data-urlencode "endpoint=<SHARED_ENDPOINT>" \
    --data-urlencode "p256dh=<ATTACKER_P256DH>" \
    --data-urlencode "auth=<ATTACKER_AUTH>"

  # Expect: HTTP 200 Ok(); DB row for <SHARED_ENDPOINT> now has UserId=<USER_B_ID>
  # and attacker-supplied P256dh/Auth, even though it originally belonged to User A.
  ```

### [NOT VULNERABLE] Reports: Login credential detail (mobile PIN) by account id
- **File**: `Controllers/ReportsController.cs` (lines 36-47)
- **Endpoint**: `GET /Reports/LoginCredentialDetail?id={accountId}`
- **Protection**: The action is gated by `[Authorize(Policy = AppPolicies.SystemAdmin)]` (line 36), the same policy that guards the full-list `LoginCredentials` action (line 28-33) and the Excel export `LoginCredentialsExcel` (line 49-50). All three expose the same underlying dataset from `residentAccountService.GetCredentialsAsync()` (which returns credentials for every account, not scoped to any particular user). Since a caller who can reach `LoginCredentialDetail?id=X` for an arbitrary `X` is, by definition, a System Admin who can already retrieve every row (including account `X`'s) via `LoginCredentials` or `LoginCredentialsExcel`, varying `id` does not cross a privilege/trust boundary — it's parameter tampering within a resource the caller is already fully authorized to view in bulk. This is same-privilege-level access, not IDOR (no "another user's private resource is disclosed to a lower-privileged caller" — the caller is the same admin who owns full access either way). The class-level `[ModuleAuthorize(AppModules.Raporlar)]` on the controller is superseded by the stricter method-level `SystemAdmin` policy on this and the other two credential actions, so residents/other roles cannot reach this action at all (also blocked by `SakinAreaRestrictionFilter` for the Sakin role since `ReportsController` sits outside `Mobile`/`Identity` areas).
- **Note**: Flagged by architecture.md as an area to verify due to the sensitivity of `Account.MobilePassword` (plaintext-stored resident PIN) exposure, and that concern is legitimate at a data-sensitivity level (any bug that widened `AppPolicies.SystemAdmin` or a future refactor removing the attribute would be high-impact), but as currently coded there is no IDOR: the authorization check is present, correctly scoped to the highest privilege role, and consistent across the list/detail/export variants of this report.

### [NOT VULNERABLE] GiderController Detay/Duzenle (LoadEditableAsync) — resident ownership check present
- **File**: `Areas/Mobile/Controllers/GiderController.cs` (lines 203-249, 251-329, 354-381)
- **Endpoint**: `GET/POST /m/Gider/Detay?id=`, `GET/POST /m/Gider/Duzenle?id=`
- **Protection**: `LoadEditableAsync` (354-381) and `Detay` (215-224) both fetch the `MahsupIslem` for the requested `LedgerTransaction` id and require `mahsup is not null && await scope.CanAccessUnitAsync(User, mahsup.UnitId)` for any resident caller; non-residents are additionally gated by `permissionService.CanWrite(User, AppModules.Muhasebe)` for the write path. A resident supplying another unit's `LedgerTransaction` id gets `NotFound()`. Attachment sub-queries within these paths (`BuildAttachmentSummariesAsync`, line 410) correctly filter by both `EntityType == nameof(LedgerTransaction)` and `EntityId == ledgerTransactionId`, so no cross-entity confusion here (unlike the `Ek` action above).
  - Sub-checks: `EkSil` (attachment delete, lines 331-351) and `MahsupSil` (mahsup delete, lines 416-429) both start with `if (scope.IsResident(User)) { return Forbid(); }`, fully blocking residents before any id lookup — no owner-vs-owner IDOR surface. `EkSil`'s lookup also correctly scopes by `Id + EntityType + EntityId` (line 342).

### [NOT VULNERABLE] KasaBankaController Detay (cash/bank statement) by id — Sakin has no module access, verified in seed data
- **File**: `Areas/Mobile/Controllers/KasaBankaController.cs` (lines 21-45)
- **Endpoint**: `GET /m/KasaBanka/Detay?kind={cash|bank}&id={cashBoxId|bankAccountId}`
- **Protection**: Multi-part verification confirms Sakin cannot reach this action at all:
  1. `[ModuleAuthorize(AppModules.KasaBanka)]` **is** applied at the class level (`KasaBankaController.cs:10`), so `ModuleAuthorizeAttribute.OnAuthorizationAsync` (`Services/ModuleAuthorizeAttribute.cs:16-41`) runs on every action including `Detay`.
  2. `Program.cs:302-357` (`SeedRolePermissionsAsync`) shows the Sakin role's permission tuple is `(Write: [Muhasebe, Talepler], View: [Panel, Daireler, Aidatlar, Duyurular])` (line 321-323). `AppModules.KasaBanka` appears in **neither** list for `AppRoles.Sakin` — only `MuhasebeGorevli` gets `KasaBanka` (line 312).
  3. `PermissionService.HasAccess` (`Services/PermissionService.cs:32-53`) is default-deny: if `(role, module)` is absent from the `RolePermissions` map (as it is for Sakin+KasaBanka, since the seed never inserts that row), the loop falls through and returns `false` — there is no implicit-allow fallback.
  4. The seed logic (`Program.cs:326-350`) is additive/idempotent ("yalnızca eksik satırları ekler, mevcut ayarları ezmez") and only ever inserts rows for the modules explicitly listed per role, so no migration or later seed pass adds a KasaBanka row for Sakin.
  - Net result: a Sakin-role caller hitting `/m/KasaBanka/Detay` gets HTTP 403 from `ModuleAuthorizeAttribute` before the action body (and its `id`-driven `detailService.BuildAsync` call) ever executes. This is a missing-authorization concern only if permission seeding were ever wrong (it isn't, per the trace above), and CashBox/BankAccount are explicitly site-wide (not resident-owned) per project context, so even for a hypothetical staff role with KasaBanka access, there is no per-owner IDOR concept to violate — every staff account with the module permission is intended to see all cash/bank accounts.
