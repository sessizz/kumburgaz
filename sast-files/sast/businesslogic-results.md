# Business Logic Analysis Results: Kumburgaz

## Executive Summary
- Scenarios analyzed: 11 (1, 2, 2b, 3–10)
- Exploitable: 6
- Likely Exploitable: 5
- Not Exploitable: 0
- Needs Manual Review: 0

## Findings

### [EXPLOITABLE] Resident self-approves dues credit via unbounded mahsup submission
- **Category**: Workflow bypass / approval-step elimination
- **File**: `Areas/Mobile/Controllers/GiderController.cs` (lines 97-180), `Services/MahsupService.cs` (lines 29-118)
- **Endpoint**: `POST /m/Gider/Yeni`
- **Business Rule Violated**: A resident-submitted expense-offset (mahsup) request should require staff review/approval before it reduces the resident's dues balance.
- **Issue**: `GiderController.Yeni` validates only that `Amount > 0`, `CategoryId` is set, `UnitId` is owned by the resident (`scope.CanAccessUnitAsync`), and — for residents specifically — that at least one photo was attached. None of these is an approval gate; they are all client-suppliable at submission time. On success it calls `mahsupService.CreateAsync(...)` synchronously in the same request, which immediately calls `collectionService.CreateAsync(...)` (`MahsupService.cs:63-71`) — this allocates the amount against the resident's own open `DuesInstallment` via `CollectionService.SaveCollectionAndReallocateAsync`, setting `DuesInstallment.Status` to `Paid`/`PartiallyPaid` and reducing `RemainingAmount` before any human has looked at the receipt photo. Confirmed via the model: `MahsupIslem` (`Models/DomainModels.cs:638-658`) has only `IsDeleted`/`DeletedAt`/`DeletedByUserId` (soft-delete fields) — no `Status`, no `Pending`/`Approved` enum, no reviewer field. A grep of the whole codebase for a staff approval action on mahsup found none; the only staff-side action on a mahsup is `MahsupSil` (delete, `GiderController.cs:416-429`), which is a destructive undo, not an approval gate.
- **Impact**: A resident can single-handedly clear or reduce their own dues obligation by submitting a mahsup with any photo (no validation that the photo depicts a real receipt matching the amount/category) — full compromise of the accounting-integrity control this workflow is meant to provide (the "approval" is fictional; it happens at photo-attach time, not at review time).
- **Proof**:
```csharp
// GiderController.cs:140-153 — no pending/staff-approval gate before crediting
if (isMahsup)
{
    await mahsupService.CreateAsync(new MahsupService.MahsupCreateRequest(
        model.UnitId!.Value, model.CategoryId!.Value, model.Amount!.Value,
        model.Description, photos, userId, userName));
    TempData["MobileSuccess"] = "Mahsuplu gider kaydedildi.";
}

// MahsupService.cs:63-71 — dues are reduced synchronously, same request
var collectionId = await collectionService.CreateAsync(new CollectionCreateViewModel
{
    DuesInstallmentId = anchor.Id,
    Date = today,
    Amount = request.Amount,
    PaymentChannel = PaymentChannel.Cash,
    AccountKey = FinancialAccountHelper.CashKey(cashBox.Id),
    Note = ...
});

// Models/DomainModels.cs:638-658 — MahsupIslem has no Status/Pending field
public class MahsupIslem : ISoftDeletable
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    ...
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
}
```
- **Remediation**: Add a `Status` (Pending/Approved/Rejected) column to `MahsupIslem`. On resident submission, create the `MahsupIslem` + `Attachment` records only (status `Pending`); do NOT call `collectionService.CreateAsync` at that point. Add a staff-only `Onayla`/`Approve` action gated by `permissionService.CanWrite(User, AppModules.Muhasebe)` that, on approval, performs the `CollectionService.CreateAsync` allocation. Reject/delete should discard the pending record without touching dues.
- **Dynamic Test**:
  ```
  1. Log in as a resident (Sakin) with access to Unit X, which has an open DuesInstallment with RemainingAmount = 500 TL.
  2. POST /m/Gider/Yeni with UnitId=X, CategoryId=<any active Gider category>, Amount=500, Fotograflar=<any small image>.
  3. Observe TempData success "Mahsuplu gider kaydedildi." and redirect to /m/Gider.
  4. Check /m/Daireler or the dues report for Unit X: RemainingAmount should now be 0 and Status=Paid — instantly, with no staff action taken in between.
  ```

### [EXPLOITABLE] Mahsup amount is not bounded by the unit's actual debt — manufactured advance credit
- **Category**: Quantity/numeric limit abuse
- **File**: `Services/MahsupService.cs` (lines 29-34), `Services/CollectionService.cs` (lines 89-254, esp. 187-245)
- **Endpoint**: `POST /m/Gider/Yeni`
- **Business Rule Violated**: Mahsup amount should be capped at (or require staff override to exceed) the unit's currently open dues balance / actual receipt amount.
- **Issue**: `MahsupService.CreateAsync` (`MahsupService.cs:31-34`) only checks `request.Amount <= 0`. It never reads `anchor.RemainingAmount` for comparison, and there is no per-transaction ceiling anywhere in the method or in `CollectionCreateViewModel`. The amount flows unmodified into `CollectionService.SaveCollectionAndReallocateAsync`, which picks a single `targetInstallment` (the anchor passed from `MahsupService`) and restricts allocation to that installment only (`CollectionService.cs:190-194`, comment: "fazlasi avans olarak kalir" — "excess remains as advance"). The allocation loop (`CollectionService.cs:222-245`) applies `Math.Min(remaining, installment.RemainingAmount)` to the installment and stops; any `remaining` left over is never allocated to any `CollectionAllocation` row, so it sits as an unallocated balance on the `Collection`. This unallocated amount functions as a running advance credit for the unit that will silently offset the unit's future dues installments. No configured max-per-transaction limit exists anywhere in the codebase (searched `MahsupCreateRequest`, `CollectionCreateViewModel`, and related validation — none found).
- **Impact**: A resident can submit, e.g., Amount=50000 against a 500 TL receipt photo. The 500 TL (or whatever the open installment's remaining balance is) clears that installment; the remaining ~49,500 TL becomes an unallocated advance tied to the resident's unit, silently reducing/eliminating dues obligations for months or years to come — a direct, self-service financial fraud path with no staff verification of the underlying expense.
- **Proof**:
```csharp
// MahsupService.cs:31-34 — only a positivity check, no upper bound, no comparison to anchor.RemainingAmount
if (request.Amount <= 0)
{
    throw new MahsupValidationException("Tutar sıfırdan büyük olmalıdır.");
}
// anchor is fetched (line 48-53) but RemainingAmount is never compared to request.Amount

// CollectionService.cs:190-194 — excess intentionally left as "avans" per the code comment
if (targetInstallment is not null)
{
    // Belirli bir taksit secildiyse odeme sadece o taksite uygulanir; tutar taksidi asarsa
    // fazlasi avans olarak kalir, diger donemlerin borclarina sizmaz.
    openInstallmentsQuery = openInstallmentsQuery.Where(x => x.Id == targetInstallment.Id);
}
```
- **Remediation**: In `MahsupService.CreateAsync`, reject (or route to mandatory staff approval — see Finding 1) any request where `request.Amount > anchor.RemainingAmount` unless the caller has `Muhasebe` write access. Alternatively/additionally, enforce a configurable absolute per-transaction cap for resident-submitted mahsups.
- **Dynamic Test**:
  ```
  1. Log in as a resident with Unit X, open DuesInstallment RemainingAmount = 500 TL.
  2. POST /m/Gider/Yeni with UnitId=X, CategoryId=<Gider category>, Amount=50000, photo attached.
  3. Confirm the installment for Unit X is now Status=Paid, RemainingAmount=0.
  4. Query the Collections table (or a subsequent dues run) for Unit X: Collection.Amount=50000 while total CollectionAllocation.AppliedAmount for that Collection = 500 — the ~49500 delta is an unallocated advance that will suppress future dues charges for Unit X.
  ```

### [EXPLOITABLE] Race condition on duplicate collection submission (double-crediting a payment)
- **Category**: Race condition / double-submission
- **File**: `Controllers/CollectionsController.cs` (lines 79-160, `CreateForUnit`; lines 171-190, `Create`); `Services/CollectionService.cs` (lines 40-48 `CreateAsync`, 89-254 `SaveCollectionAndReallocateAsync`); `Models/DomainModels.cs` (lines 485-541, `DuesInstallment`/`Collection` entities); `Data/ApplicationDbContext.cs` (lines 218-251, model configuration)
- **Endpoint**: `POST /Collections/CreateForUnit`, `POST /Collections/Create`
- **Business Rule Violated**: The same real-world cash/bank deposit must be recordable exactly once — no duplicate crediting of a unit's dues installment or unit balance from concurrent/replayed submissions of the same payment.
- **Issue**: `SaveCollectionAndReallocateAsync` reads open installments (`db.DuesInstallments...Where(x => x.RemainingAmount > 0)`, lines 187-201), then later writes `installment.RemainingAmount -= applied` and calls `db.SaveChangesAsync()` (lines 222-247), all wrapped in a per-request `Database.BeginTransactionAsync()` (lines 127-129). This transaction only guarantees atomicity *within* a single request — it does not prevent two concurrent requests each reading the same `RemainingAmount > 0` state before either commits. Two concurrent POSTs (double-click, browser back-button resubmit, or a scripted replay) for the identical payment will each independently insert a new `Collection` row and reduce `DuesInstallment.RemainingAmount` by the same applied amount. Verified in the EF model: `Collection` has **no** `[Timestamp]`/`RowVersion` concurrency token, **no** unique index on `(ReferenceNo, Amount, Date, UnitId)`, and **no** application-level idempotency key check anywhere in `CollectionService` or `CollectionsController`. The only unique index found is on `DuesInstallment(BillingGroupId, Period, UnitId)`, which is unrelated to duplicate-payment prevention. Contrast: the CSV import path *does* have deduplication via `ImportBatchService.BuildNormalizedKey` + `HasCommittedDuplicateAsync` (`CollectionsController.cs:359-376`, `CashBankController.cs:198-214`), proving the developers know how to build idempotency keys — but the interactive UI paths never use this mechanism.
- **Impact**: A double-click or network retry on the "Tahsilat ekle" (add collection) button silently records the same cash deposit twice: the unit's dues balance is over-credited, and the cash/bank account's transaction history shows one extra inbound transaction that never happened. This directly corrupts financial statements and unit balance reporting, and is exploitable by any legitimately-permissioned Tahsilatlar/Muhasebe user (accidentally) or an attacker with valid session cookies replaying the POST (deliberately) to inflate a resident's paid-in-full status.
- **Proof**:
  ```csharp
  // CollectionService.cs:187-247 — no locking / idempotency between the read and the write
  var openInstallmentsQuery = db.DuesInstallments
      .Where(x => x.BillingGroupId == billingGroupId && x.RemainingAmount > 0);
  ...
  var openInstallments = await openInstallmentsQuery...ToListAsync();
  var remaining = model.Amount;
  ...
  foreach (var installment in openInstallments)
  {
      var applied = Math.Min(remaining, installment.RemainingAmount);
      installment.RemainingAmount -= applied;   // no OCC token, no row lock
      ...
      db.CollectionAllocations.Add(new CollectionAllocation { ... });
      remaining -= applied;
  }
  await db.SaveChangesAsync();
  if (tx is not null) { await tx.CommitAsync(); }
  ```
  ```csharp
  // Models/DomainModels.cs:512-541 — Collection entity has no concurrency token / uniqueness constraint
  public class Collection : ISoftDeletable
  {
      public int Id { get; set; }
      ...
      public string? ReferenceNo { get; set; }  // not unique, not part of any index
      ...
  }
  ```
- **Remediation**: Add a unique DB index (e.g. on `(UnitId, ReferenceNo)` when `ReferenceNo` is non-null, or a client-generated idempotency-key column) so a second identical submission fails with a constraint violation the controller can catch and turn into a friendly "already recorded" message. Additionally/alternatively, add a `[Timestamp]` `RowVersion` to `DuesInstallment` so `SaveChangesAsync` throws `DbUpdateConcurrencyException` on a lost-update race, and reuse the existing `ImportBatchService.BuildNormalizedKey` pattern for the interactive create paths.
- **Dynamic Test**:
  ```
  # Authenticate as a Tahsilatlar-write user, then fire two identical POSTs concurrently:
  for i in 1 2; do
    curl -s -b cookies.txt -c cookies.txt \
      -X POST "https://<host>/Collections/CreateForUnit" \
      -d "unitId=42&amount=1500&date=2026-07-16&accountKey=cash:1&note=test&__RequestVerificationToken=<token>" &
  done
  wait
  # Then check: GET /CashBank/CashBox/1 (or the unit's ledger/detail page) — two Collection rows
  # with identical amount/date/unit will appear, and the target DuesInstallment.RemainingAmount
  # will have been reduced twice (or Status flips to Paid while a duplicate row also exists).
  ```

### [EXPLOITABLE] Unrestricted backdating of ledger/collection/dues entries into closed accounting periods
- **Category**: Time/date logic
- **File**: `Controllers/LedgerController.cs` (lines 116-200), `Controllers/CashBankController.cs` (lines 611-960, `ApplyTransactionOrderSlot` ~1348), `Controllers/DuesGenerationController.cs` (lines 25-32)
- **Endpoint**: `POST /Ledger/Create`, `POST /Ledger/Edit/{id}`, `POST /CashBank/CreateLedger`, `POST /CashBank/CreateCollection`, `POST /CashBank/CreateTransfer` (and their `Edit` counterparts), `POST /DuesGeneration/Generate`
- **Business Rule Violated**: Financial entries should not be freely backdated/forward-dated into an already-reported or closed period without additional control (approval, period lock, or restriction to a higher role).
- **Issue**: `model.Date` / `accrualDate` / `dueDate` are bound directly from the posted form and written to the entity with no server-side check against a "closed period" concept. A grep across the codebase for `ClosedPeriod|PeriodLock|IsClosed|LockedPeriod|DonemKapat` returned zero matches — **no period-closing mechanism exists in this codebase at all**, so any user with write access to `Muhasebe`/`Aidatlar` modules (not just SystemAdmin) can post/edit an income, expense, or dues accrual with an arbitrary past or future date at any time. `CashBankController.ApplyTransactionOrderSlot`/`BuildNextTransactionDateAsync`/`ResolveEditedTransactionDateAsync` actively normalize sub-day ordering ticks so a backdated entry sorts correctly among same-day transactions — confirming backdating is a supported, unguarded input path rather than an oversight.
- **Impact**: Any staff user with Muhasebe/Aidatlar write access can silently alter historical financial statements after period-end reports have been generated/distributed — booking a large backdated expense/income, or generating a dues accrual for a past period, with no re-validation, approval, or audit flag distinguishing it from a same-day entry (though the action itself is still captured by the generic `AuditLog` change-tracker with the true actor/timestamp — the *reported period* it lands in simply has no protection).
- **Proof**:
  ```csharp
  // Controllers/LedgerController.cs:130-141
  var entity = new LedgerTransaction
  {
      Date = DateTimeHelper.EnsureUtc(model.Date),   // fully client-controlled, no bounds check
      IncomeExpenseCategoryId = model.IncomeExpenseCategoryId,
      Amount = model.Amount,
      ...
  };
  db.LedgerTransactions.Add(entity);
  await db.SaveChangesAsync();
  ```
  ```csharp
  // Controllers/DuesGenerationController.cs:25-32
  public async Task<IActionResult> Generate(string period, DateTime accrualDate, DateTime dueDate, DuesPayerType payerType)
  {
      await duesGenerationService.GenerateForPeriodAsync(period, accrualDate, dueDate, payerType);
      ...
  }
  ```
- **Remediation**: Introduce an explicit "period lock" concept (e.g., a `ClosedPeriod` table or a `Settings.LastClosedPeriod` value) and validate `Date`/`accrualDate`/`dueDate` server-side against it in `LedgerController`, `CashBankController`, and `DuesGenerationService`; reject or require a `SystemAdmin`-only override/justification for entries dated into a closed period. At minimum, add a distinct audit-log annotation when a submitted date differs materially from `DateTime.UtcNow`.
- **Dynamic Test**:
  ```
  1. Log in as a user with Muhasebe write access (not SystemAdmin).
  2. POST /Ledger/Create with Date=2024-01-01 (a period already reported/closed) and a large Amount.
  3. Observe the entry is accepted and appears in historical reports for that period with no warning/approval step.
  4. Repeat via POST /DuesGeneration/Generate with accrualDate/dueDate set to a past fiscal period string.
  ```

### [EXPLOITABLE] Database restore performs an unvalidated, unconfirmed, non-audited full overwrite of production data
- **Category**: Workflow/data-integrity gap around a multi-step operation
- **File**: `Controllers/BackupsController.cs` (lines 45-82), `Services/BackupService.cs` (lines 60-73)
- **Endpoint**: `POST /Backups/Restore`
- **Business Rule Violated**: Restore should require a second confirmation step showing what will be overwritten (diff/manifest check against the current DB and the uploaded file), and should write an `AuditLog` entry describing the restore action, actor, and source file identity/hash.
- **Issue**: Confirmed by reading the full flow:
  1. **No file validation**: `Restore(IFormFile? file)` only checks `file is null || file.Length == 0` (line 49) — no checksum, manifest, magic-byte/format check, or verification that the uploaded file is actually a genuine backup of *this* database.
  2. **No diff/preview**: the file is saved to a temp path and passed straight to `BackupService.RestoreAsync` (line 66) with no summary shown to the admin of what will change.
  3. **No audit log for the restore action itself**: the app's only audit mechanism is `ApplicationDbContext.SaveChangesAsync`'s EF `ChangeTracker`-based interceptor (`Data/ApplicationDbContext.cs:353-385`), which only fires for entity changes made *through EF Core*. `RestoreAsync` bypasses EF Core entirely — for SQLite it does `File.Copy(filePath, target, overwrite: true)` (line 68), a raw filesystem overwrite of the whole `.db` file; for Postgres it shells out to `pg_restore --clean --if-exists` (line 72). Neither path touches `DbContext.SaveChanges`, so **no `AuditLog` row is ever created for a restore**, and since the SQLite case overwrites the very file containing the `AuditLogs` table, any trace of *who* restored *and when* is only findable via OS-level file timestamps or DB server logs, not the application's own audit trail (confirmed via grep — zero `db.AuditLogs.Add(...)` calls in `BackupsController.cs`/`BackupService.cs`).
  4. A pre-restore safety backup **is** taken (`CreateBackupAsync("restore-oncesi", ...)`, line 62) — a partial mitigation, but not surfaced to the user as part of the workflow (no "undo" link, no filename shown in the success message).
- **Impact**: A single POST with any uploaded file silently and irreversibly (from the app's perspective) replaces every collection, mahsup, ledger, dues, and account record with whatever the file contains, erasing all activity since the backup's timestamp, with zero in-app record of the action having occurred. Combined with the note that `BackupsController` is SystemAdmin-only (access control out of scope here), the remaining risk is entirely about *safety of the operation itself*: a compromised/rogue SystemAdmin account, a mis-clicked upload, or a stale backup file uploaded by mistake all produce the same silent, unauditable, unconfirmed data loss.
- **Proof**:
  ```csharp
  // Controllers/BackupsController.cs:47-53
  public async Task<IActionResult> Restore(IFormFile? file)
  {
      if (file is null || file.Length == 0)   // only non-empty check, no content validation
      {
          TempData["ActionError"] = "Geri yüklenecek yedek dosyası seçin.";
          return RedirectToAction(nameof(Index));
      }
      ...
  ```
  ```csharp
  // Services/BackupService.cs:60-73
  public async Task RestoreAsync(string filePath, CancellationToken cancellationToken = default)
  {
      await CreateBackupAsync("restore-oncesi", cancellationToken);   // safety copy, no confirmation to user
      var connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

      if (IsSqlite(connectionString))
      {
          var target = ResolveSqlitePath(connectionString);
          File.Copy(filePath, target, overwrite: true);   // raw overwrite, bypasses EF/AuditLog entirely
          return;
      }

      await RunProcessAsync(GetPgToolPath("PgRestorePath", "PG_RESTORE_PATH", "pg_restore"),
          BuildPgRestoreArgs(connectionString, filePath), connectionString, cancellationToken);  // pg_restore --clean, also bypasses AuditLog
  }
  ```
- **Remediation**:
  - Require a two-step confirmation (upload → show summary/diff of table row counts, date ranges, and a checksum of the uploaded file → explicit "confirm restore" POST).
  - Validate the uploaded file is a plausible backup of this app before overwriting the live DB.
  - Write an explicit audit-log-equivalent entry *before* the destructive operation (e.g., log to a file outside the DB, or to an external logger/SIEM) capturing actor, timestamp, uploaded filename, and file hash.
  - Surface the pre-restore safety-backup filename to the admin in the success message so recovery is discoverable without digging into the filesystem.
- **Dynamic Test**:
  ```
  1. As SystemAdmin, go to /Backups.
  2. POST /Backups/Restore with any arbitrary file (even a backup from months ago).
  3. Observe: no preview/diff shown before the POST executes, action completes immediately with only "Yedek geri yüklendi." message, and /Audit (AuditController, db.AuditLogs) shows no entry for the restore itself.
  ```

### [EXPLOITABLE] Advance/credit balance from overpaid collections (incl. resident-initiated mahsup) is auto-consumed into future dues with no cap, audit trail, or approval step
- **Category**: Refund/balance logic ambiguity
- **File**: `Services/CollectionService.cs` (`SaveCollectionAndReallocateAsync`, lines 89-254, esp. 190-245), `Services/CollectionAdvanceAllocator.cs` (full file, esp. `ApplyAsync` lines 9-78), `Services/MahsupService.cs` (`CreateAsync`, lines 29-118), `Areas/Mobile/Controllers/GiderController.cs` (mahsup entry point, resident-accessible: `isMahsup = isResident || model.IsMahsup` — residents are always forced into the mahsup path)
- **Endpoint**: `POST /Collections/Create` (staff), `POST /m/Gider/Create` (mobile/resident self-service — reachable by the "Sakin" resident role per mobile-area scoping), invoked again automatically by `Services/DuesGenerationService.cs:220` and `Controllers/DuesController.cs:156` (`CollectionAdvanceAllocator.ApplyAsync(db)` runs on every new dues-installment generation)
- **Business Rule Violated**: Advance/credit balances above a threshold should be visible to staff and require sign-off before being consumed against future dues, especially when the credit originated from a resident self-service action (mahsup).
- **Issue**: When a `Collection` amount exceeds the targeted installment's `RemainingAmount`, the excess is never written to a dedicated ledger row — it exists only implicitly as `collection.Amount - Σ(CollectionAllocation.AppliedAmount)` (comment at line 192-193: "fazlasi avans olarak kalir" / "the excess remains as advance"). `CollectionAdvanceAllocator.ApplyAsync` (auto-invoked by `DuesGenerationService` and `DuesController` whenever new installments are generated) scans all collections ordered by date, computes this same implicit credit, and — with no minimum/maximum bound, no staff notification, and no approval gate — silently allocates that credit into whichever dues installments are next open for that unit. This is reachable via a low-privilege path: `GiderController.cs` forces `isMahsup = true` for any resident ("Sakin") user, and `MahsupService.CreateAsync` accepts an arbitrary `request.Amount` (only validated `> 0`) and routes it through the same advance path.
- **Impact**: A resident (or staff acting on a resident's behalf) can build an outsized credit balance via a single oversized mahsup or manual collection; that balance is not tracked as a distinct, auditable, capped entity anywhere — it's an emergent property of `Collection.Amount` vs `Sum(Allocations)`, silently swept into future periods by a background-style allocator with no confirmation step. No report shows "unit X has a 5,000 TL unapplied advance" as a first-class figure (`Services/DuesLedgerRowService.cs:125-129` only reflects it indirectly). This overlaps with a pre-existing internally documented issue (`review.md` M8) about `CollectionAdvanceAllocator` distributing group-level advances inconsistently with `CollectionService`.
- **Proof**:
```csharp
// CollectionAdvanceAllocator.ApplyAsync — auto-invoked on every dues generation, no threshold/approval
var credit = collection.Amount - collection.Allocations.Sum(x => x.AppliedAmount);
if (credit <= 0) { continue; }
// ... reserves against negative opening balance first, then:
foreach (var installment in installments)
{
    if (credit <= 0) break;
    var applied = Math.Min(credit, installment.RemainingAmount);
    installment.RemainingAmount -= applied;
    // ... status update, CollectionAllocation added, no cap check, no notification
    credit -= applied;
}
```
```csharp
// MahsupService.CreateAsync — only validates Amount > 0, no upper bound vs anchor.RemainingAmount
if (request.Amount <= 0) { throw new MahsupValidationException("Tutar sıfırdan büyük olmalıdır."); }
...
var collectionId = await collectionService.CreateAsync(new CollectionCreateViewModel
{
    DuesInstallmentId = anchor.Id,
    Amount = request.Amount,   // unbounded; excess becomes implicit "advance"
    ...
});
```
```csharp
// GiderController — residents always routed through mahsup
var isMahsup = isResident || model.IsMahsup;
```
- **Remediation**: Introduce a first-class `UnitAdvanceBalance`/ledger entity written whenever `Collection.Amount > Σ(Allocations)`, rather than deriving it ad hoc. Require staff acknowledgement/approval before `CollectionAdvanceAllocator` consumes any credit above a configurable threshold, and add an upper bound to `MahsupService.CreateAsync`'s `request.Amount` (cap at the anchor installment's `RemainingAmount`, or route any excess into a separate flagged-for-review state). Surface accumulated advance balances in a per-unit report so staff have visibility before the next generation cycle silently spends it down.
- **Dynamic Test**:
  ```
  1. Log in as a resident ("Sakin") mobile user for a unit with a small open dues
     installment (e.g. 500 TL remaining).
  2. POST to /m/Gider/Create with Amount = 5000 (far exceeding the 500 TL debt) and a
     valid expense category/photo — MahsupService.CreateAsync only checks Amount > 0.
  3. Confirm the mahsup is accepted: the anchor installment's RemainingAmount goes to 0
     (Paid), and Collection.Amount (5000) - Σ(Allocations) (500) = 4500 TL of
     unallocated "advance" now exists for that unit, with no report/alert surfacing it.
  4. As staff, generate the next period's dues installments and observe
     CollectionAdvanceAllocator.ApplyAsync silently consume the 4500 TL advance into
     the new installment(s) with no confirmation prompt or audit entry distinguishing
     it from a normal payment.
  ```

---

### [LIKELY EXPLOITABLE] Mahsup unit/category integrity relies entirely on client-submitted UnitId (category/amount sanity gap)
- **Category**: Entitlement bypass (financial) — category/amount business-rule gap only (unit-ownership IDOR check itself is out of scope)
- **File**: `Areas/Mobile/Controllers/GiderController.cs` (lines 111-133), `Services/MahsupService.cs` (lines 39-44)
- **Endpoint**: `POST /m/Gider/Yeni`
- **Business Rule Violated**: The claimed expense category should be sanity-checked against the claimed amount before that amount is auto-credited to a resident's dues balance.
- **Issue**: `GiderController.Yeni` only checks that `CategoryId` has a value; `MahsupService.CreateAsync` then loads the category with `x.Id == request.CategoryId && x.Active && x.Type == CategoryTypeHelper.Gider` (`MahsupService.cs:39-41`) — an existence/active/type check only. There is no per-category maximum amount, no category-tier logic, and no cross-check that an amount is plausible for the selected category. Combined with Finding 2 (no cap vs. the unit's actual debt), a resident with legitimate access to any single unit can select an arbitrary low-cost-sounding category and submit an arbitrarily large amount, laundering it into dues credit. (The unit-ownership check `scope.CanAccessUnitAsync` at line 124 is sound and out of scope.)
- **Concern**: This is a genuine gap (no category-appropriate bound exists in code — no `MaxAmount`/per-category-limit field on `IncomeExpenseCategory`), but its severity is really a compounding factor of the "mahsup amount unbounded" finding rather than an independently exploitable primitive — the category field has no security purpose beyond bookkeeping labeling in the current design. Rated Likely Exploitable rather than Exploitable because there is no distinct additional server-side gate to defeat beyond what the amount-bound gap already defeats.
- **Remediation**: If category-based amount bounds are a desired control, add an optional `MaxAmount`/`SuggestedMaxAmount` field to `IncomeExpenseCategory` and validate `request.Amount` against it in `MahsupService.CreateAsync`, flagging or blocking outliers for staff review. Best implemented as part of the mahsup-approval-queue remediation.
- **Dynamic Test**:
  ```
  1. As a resident with access to Unit X, note any active Gider-type category (e.g., a low-cost-sounding one).
  2. POST /m/Gider/Yeni with that CategoryId and Amount=50000, photo attached.
  3. Confirm the request succeeds and credits dues exactly as in the amount-bound finding — no category-vs-amount check ever fires.
  ```

### [LIKELY EXPLOITABLE] Opening balances can be silently rewritten with no bounds, reason, or maker-checker
- **Category**: Balance/transfer logic — financial statement corruption
- **File**: `Controllers/OpeningBalancesController.cs` (lines 37-95, `Save`)
- **Endpoint**: `POST /OpeningBalances/Save`
- **Business Rule Violated**: Opening balance edits — which directly change how future dues payments are auto-allocated (`CollectionService.cs:207-219`, the "devir borcu" negative-opening-balance auto-payoff logic) — should require a second approver for material changes and be individually audit-logged with the prior value.
- **Issue**: `Save` accepts parallel arrays `unitIds[]`/`balances[]`/`dates[]` from the POST body and, for every matched unit, directly overwrites `unit.OpeningBalance` and `unit.OpeningBalanceDate` (lines 82-83) with no bound check — `decimal.TryParse` (line 61) accepts any magnitude and sign, there is no comparison against existing collections/ledger history, no "reason for change" field, no maker-checker/approval workflow, and **no audit log entry is written at all**. The change is applied immediately via a single `db.SaveChangesAsync()` (line 88) and can silently rewrite an arbitrary number of units' opening balances in one request.
- **Impact**: Any user holding Muhasebe module write access can instantly and untraceably rewrite the financial starting point for any subset of units, with no record of who changed what from what value. A maliciously or erroneously set opening balance can make a unit appear debt-free (masking real arrears) or artificially indebted, without any built-in trace for later investigation.
- **Proof**:
  ```csharp
  // OpeningBalancesController.cs:39-88
  public async Task<IActionResult> Save(int[] unitIds, string[] balances, string[] dates, int? blockId = null)
  {
      ...
      if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var newValue))
      {
          invalidCount++;
          continue;
      }
      // no min/max bound check on newValue
      ...
      if (unit.OpeningBalance != newValue) { unit.OpeningBalance = newValue; changed = true; }  // no audit log, no approval
      if (unit.OpeningBalanceDate != newDate) { unit.OpeningBalanceDate = newDate; changed = true; }
      if (changed) changedCount++;
  }
  if (changedCount > 0)
      await db.SaveChangesAsync();   // immediate, unlogged, unreviewed
  ```
- **Concern**: The endpoint is properly gated by `[ModuleAuthorize(AppModules.Muhasebe)]` at the class level, so this is scoped purely to the business-rule gap once a legitimate Muhasebe-write user calls it. Whether this rises to "Exploitable" vs "Likely Exploitable" depends on how broadly Muhasebe write access is granted in this deployment's role matrix (not verifiable from code alone); the code-level gap itself (no bounds, no audit trail, no maker-checker) is unambiguous.
- **Remediation**: Bound the accepted decimal range to a sane financial magnitude; require a mandatory "reason" text field; write an audit-log entry (old value → new value, user, timestamp) for every changed unit; for changes above a configurable materiality threshold, require a second user's approval (maker-checker) before the change takes effect.

### [LIKELY EXPLOITABLE] Cash/bank account opening balance can be reset to zero or rewritten in one click, erasing financial history
- **Category**: Balance/transfer logic — financial statement corruption
- **File**: `Controllers/CashBankController.cs` (lines 480-518 `UpdateOpeningBalance`, 520-540 `DeleteOpeningBalance`; compare 452-478 `DeleteAccount`)
- **Endpoint**: `POST /CashBank/DeleteOpeningBalance`, `POST /CashBank/UpdateOpeningBalance`
- **Business Rule Violated**: Opening-balance changes on cash/bank accounts that already have transaction history should require the same protection `DeleteAccount` applies before allowing deletion (block the action or require an extra confirmation step) and should be audit-logged.
- **Issue**: `DeleteOpeningBalance` (lines 520-540) unconditionally sets `bank.OpeningBalance = 0m` (line 528) or `cash.OpeningBalance = 0m` (line 534) for any account ID with a single authenticated POST — no check for existing `Collections`/`LedgerTransactions` referencing the account, no confirmation step, no audit trail. This is a stark contrast to `DeleteAccount` in the same controller (lines 452-478), which explicitly checks `hasCollections`/`hasLedger` via `db.Collections.AnyAsync(...)`/`db.LedgerTransactions.AnyAsync(...)` (lines 454-455) and refuses the destructive action ("İşlem görmüş hesap silinemez") if the account has transaction history — proving the developers are aware of this exact risk pattern but did not apply it to `DeleteOpeningBalance`/`UpdateOpeningBalance`. `UpdateOpeningBalance` (lines 480-518) similarly accepts any decimal via `TryReadFormDecimal(..., _ => true)` (validator always returns `true` — no bound, not even a sign check) and immediately persists it with `db.SaveChangesAsync()` (line 515), again with no audit log.
- **Impact**: Any Muhasebe/KasaBanka-write user can instantly zero out or arbitrarily rewrite a cash box's or bank account's opening balance even after it has years of collection/ledger history attached, corrupting all downstream running-balance calculations with no record of the prior value or who made the change. This is strictly weaker protection than the sibling `DeleteAccount` action in the same controller.
- **Proof**:
  ```csharp
  // CashBankController.cs:520-540
  public async Task<IActionResult> DeleteOpeningBalance(string kind, int id)
  {
      if (kind == "bank")
      {
          var bank = await db.BankAccounts.FindAsync(id);
          if (bank is null) return NotFound();
          bank.OpeningBalance = 0m;              // unconditional, no history check, no audit
      }
      else
      {
          var cash = await db.CashBoxes.FindAsync(id);
          if (cash is null) return NotFound();
          cash.OpeningBalance = 0m;
      }
      await db.SaveChangesAsync();
      ...
  }
  ```
  ```csharp
  // Contrast — CashBankController.cs:452-460, the protection that DeleteOpeningBalance lacks
  public async Task<IActionResult> DeleteAccount(string kind, int id)
  {
      var hasCollections = await db.Collections.AnyAsync(x => kind == "bank" ? x.BankAccountId == id : x.CashBoxId == id);
      var hasLedger = await db.LedgerTransactions.AnyAsync(x => kind == "bank" ? x.BankAccountId == id : x.CashBoxId == id);
      if (hasCollections || hasLedger)
      {
          TempData["ActionError"] = "İşlem görmüş hesap silinemez. Pasifleştirebilirsiniz.";
          return RedirectToDetail(kind, id);
      }
      ...
  }
  ```
- **Concern**: Gated by `[ModuleAuthorize(AppModules.KasaBanka)]` at the class level (authorization out of scope). Severity classification depends on how widely KasaBanka write access is assigned in the live role matrix, not determinable from source alone — the code-level absence of any history check or audit trail is confirmed and unambiguous.
- **Remediation**: In `UpdateOpeningBalance`/`DeleteOpeningBalance`, mirror `DeleteAccount`'s `hasCollections`/`hasLedger` check — require explicit typed confirmation (or block entirely) when the account already has transaction history, add a plausibility/range check on the new value, and write an audit-log entry recording the previous opening balance, new value, user, and timestamp.

### [LIKELY EXPLOITABLE] Mahsup evidence and description remain resident-editable indefinitely after credit is applied, with no approval/status gate
- **Category**: Workflow bypass (evidence integrity)
- **File**: `Areas/Mobile/Controllers/GiderController.cs` (lines 278-329, 353-405), `Models/DomainModels.cs` (`MahsupIslem`, lines 638-660)
- **Endpoint**: `POST /m/Gider/Duzenle/{id}`
- **Business Rule Violated**: Attachments/description backing an already-credited financial transaction (`MahsupIslem`) should be immutable, version-tracked, or gated behind a re-approval step once the corresponding `Collection`/dues credit has posted.
- **Issue**: `MahsupIslem` has no `Status`/`Approved` field at all — it is only `ISoftDeletable`. The credit is applied at creation time and never re-validated. `LoadEditableAsync` (lines 354-381) permits the owning resident to reach `Duzenle` for their own mahsup at any time with no time window or status check, and the POST handler lets the resident update `Description` freely and add new photos via `SaveAttachmentsAsync` (lines 383-405) with no restriction on how long after creation this happens. **Correction to the original threat-model description**: residents cannot actually *delete/swap out* existing photos — `EkSil` (lines 331-350) explicitly `Forbid()`s residents (`if (scope.IsResident(User)) return Forbid();`, line 336-339), and `SaveAttachmentsAsync` only appends new `Attachment` rows, never removes old ones. So the original evidence used at approval time is preserved, not destroyed. However, the residual risk stands: a resident can add new, different photos or freely rewrite the description at any later date with no re-review trigger, no diff/versioning of the description text, and no flag distinguishing "reviewed at approval" content from "edited afterward" content.
- **Impact**: Reduced evidentiary trust for already-credited mahsup transactions; a resident could pad the record with a more convincing photo after the fact, or alter the narrative description, without any of this being surfaced to staff as a post-approval change. Amount/category are correctly locked (`amountCategoryEditable = mahsup is null`), which limits blast radius to non-monetary fields.
- **Proof**:
  ```csharp
  // Areas/Mobile/Controllers/GiderController.cs:366-374
  var isResident = scope.IsResident(User);
  if (isResident)
  {
      // Sakin yalnizca kendi mahsubunu duzenleyebilir (tutar/kategori haric).
      if (mahsup is null || !await scope.CanAccessUnitAsync(User, mahsup.UnitId))
      {
          return (null, null, NotFound());
      }
  }
  // no time-window / status check beyond ownership
  ```
  ```csharp
  // Areas/Mobile/Controllers/GiderController.cs:336-339 (mitigating control found)
  if (scope.IsResident(User))
  {
      return Forbid();   // residents cannot delete existing evidence photos
  }
  ```
- **Concern**: Whether this rises to a real exploit depends on how staff actually review mahsup evidence in practice (do they review immediately at creation and never revisit?) — a process/product-design question the code alone can't fully answer, hence "Likely Exploitable."
- **Remediation**: Add a `Status` (Pending/Approved) or `ReviewedAt` timestamp to `MahsupIslem`; once approved, either lock `Duzenle` entirely for residents or route edits through a re-approval workflow. Timestamp/version each `Attachment` and description edit distinctly so staff can see what existed at original approval time vs. what was added later.
- **Dynamic Test**:
  ```
  1. As a resident, create a mahsup via POST /m/Gider/Yeni with photo A; confirm credit posts immediately.
  2. Days later, POST /m/Gider/Duzenle/{id} with a new Description and an additional photo B.
  3. Observe the edit succeeds silently with TempData "Gider güncellendi." and no re-review/notification to staff.
  4. Confirm in DB that both photo A and B are present (attachments are additive, not destructive) but there is no field indicating which was present at original approval time.
  ```

### [LIKELY EXPLOITABLE] CashBank CSV import auto-infers transaction type/category via fuzzy text matching with no confidence surfacing or exact-match requirement for dues allocation
- **Category**: Workflow bypass via automation gap
- **File**: `Controllers/CashBankController.cs` — `CommitImport` (lines 170-376), `BuildImportPreviewRow` (1156-1243), `InferImportType` (1412-1436), `MatchDuesOption` (1438-1482), `MatchCategory` (1493-1519)
- **Endpoint**: `GET /CashBank/PreviewImport` → `POST /CashBank/CommitImport` (staff-only, `[ModuleAuthorize]`-gated)
- **Business Rule Violated**: Low-confidence fuzzy matches should force manual row confirmation rather than silently committing; unit/dues matches should require an exact code match, not token-overlap scoring, for anything that touches per-resident debt.
- **Issue**: `MatchDuesOption` (1438-1482) does two-stage matching: it first extracts unit-code-like tokens (regex `\b[\p{L}]{1,4}\s*[-/]?\s*\d+[a-zA-Z]?\b`) from the free-text `matchText` and filters dues options to those whose `SearchText`/`Text` *contains* that substring (not exact match), then falls back to plain token-overlap scoring (`Score = tokens.Count(token => haystack.Contains(token))`) with no minimum-score/confidence threshold. `MatchCategory` (1493-1519) has the identical no-threshold token-scoring pattern. Critically, the preview UI (`Views/CashBank/ImportPreview.cshtml`) only shows a warning when a match is **null** (lines 1207-1210) — a low-confidence, wrong-unit match is displayed identically to a high-confidence correct one; no score/confidence is ever surfaced. `CommitImport` (216-234) only validates that `row.DuesInstallmentId` is non-null and exists in the DB — it never cross-checks `row.Amount` against `installment.RemainingAmount`.
- **Impact**: On a large CSV batch, a row whose free-text description happens to token-overlap with a different unit's resident name/unit code can be silently pre-selected as the "match," and because the UI gives no confidence signal, a busy operator reviewing dozens/hundreds of rows is unlikely to catch it before clicking "Import et." Result: a collection gets applied to the wrong unit's dues installment, incorrectly reducing that unit's `RemainingAmount`/debt while leaving the true payer's debt unresolved.
- **Proof**:
```csharp
// MatchDuesOption — substring unit-code match, then unbounded token-overlap fallback, no confidence threshold
return options
    .Select(x => new { Option = x, Haystack = NormalizeSearchText($"{x.SearchText} {x.Text}") })
    .Select(x => new { x.Option, Score = tokens.Count(token => x.Haystack.Contains(token, StringComparison.OrdinalIgnoreCase)) })
    .Where(x => x.Score > 0)               // <-- any score > 0 accepted, no minimum/confidence gate
    .OrderByDescending(x => x.Score)
    .ThenByDescending(x => x.Option.RemainingAmount > 0)
    .ThenBy(x => x.Option.Text.Length)
    .Select(x => x.Option)
    .FirstOrDefault();
```
```csharp
// CommitImport — no amount vs installment.RemainingAmount cross-check before creating the Collection
var installment = await db.DuesInstallments.AsNoTracking()
    .FirstOrDefaultAsync(x => x.Id == row.DuesInstallmentId.Value);
if (installment is null) { errors.Add(...); continue; }
importRows.Add(new CashBankImportOperation(row, date, amount, installment, null, normalizedKey)); // amount unchecked
```
- **Concern / Mitigating factor**: This is not a directly attacker-forgeable server-side bypass — `CommitImport` runs behind a human-reviewed, editable preview form (every row's Type/DuesInstallmentId/Amount/Category is a plain `<select>`/`<input>` the operator can change before submit), and the feature is staff-module-gated. The exploit path requires either staff inattention on a large batch, or a malicious insider crafting the source CSV description text to nudge the fuzzy match toward a specific unit while relying on reviewer fatigue. Because the UI never distinguishes a confident match from an incidental one, the human-review safety net is weaker than it looks. Rated Likely Exploitable (not confirmed Exploitable) because it depends on an inattentive/negligent reviewer rather than a pure server-side gap.
- **Remediation**: Compute and store a match confidence/score alongside each preview row; require `Score` above a minimum threshold (or an exact unit-code equality match) before pre-filling `DuesInstallmentId`, and visibly flag ambiguous/low-confidence rows (e.g. red "Düşük güven eşleşmesi — kontrol edin" status). Additionally add an amount-vs-`RemainingAmount` sanity check in `CommitImport` before committing.
- **Dynamic Test**:
  ```
  1. As a staff user with CashBank write access, prepare a CSV where one row's
     description contains only a resident's first name that happens to also appear
     in another unit's dues option text (e.g. description "Ahmet ödeme 500", where
     two different units both have owners named "Ahmet").
  2. POST to /CashBank/PreviewImport and observe the preview row: Status shows "Hazır"
     (no warning) even though the DuesInstallmentId was picked via a single-token match.
  3. Submit the form as-is (no edits) to /CashBank/CommitImport.
  4. Verify in DuesInstallments/CollectionAllocations that the payment was applied to
     the wrong unit's installment (RemainingAmount reduced on the wrong unit).
  ```
