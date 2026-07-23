# Missing Auth/Authz Analysis Results: Kumburgaz (Site/Apartment Management Web App)

## Executive Summary
- Endpoints/controllers analyzed: 22 groups (covering all ~22 desktop controllers, all 10 mobile controllers, and the Identity area)
- Vulnerable: 1
- Likely Vulnerable: 0
- Not Vulnerable: 21
- Needs Manual Review: 0

## Findings

### [VULNERABLE] SearchController.Global — cross-module data leak via single `Raporlar` permission
- **File**: `Controllers/SearchController.cs` (class attribute line 11, action lines 14-195)
- **Endpoint**: `GET /Search/Global?term=`
- **Issue**: Authentication is enforced (`ModuleAuthorizeAttribute` challenges unauthenticated users). However, the only authorization check on this action is the class-level `[ModuleAuthorize(AppModules.Raporlar)]`, which requires nothing more than **view** access to the `Raporlar` (Reports) module. The action then queries and returns data from `Units`, `Accounts`, `Collections` (Tahsilatlar), `LedgerTransactions`/`BankAccounts`/`CashBoxes` (Muhasebe/KasaBanka), `IncomeExpenseCategories`, `DocumentRecords`, and `ServiceRequests` — i.e., it aggregates read access to nearly every financial and operational module in the app behind one permission check, without verifying the caller has view rights on `Tahsilatlar`, `Muhasebe`, `KasaBanka`, `Aidatlar`, or `Hesaplar` individually.
- **Confirmed via seed data** (`Program.cs`, `SeedRolePermissionsAsync`, lines 302-357): the `Personel` and `SadeceGoruntuleme` roles are seeded with view access to **only** `[Panel, Raporlar]` — explicitly excluding `Tahsilatlar`, `Muhasebe`, `KasaBanka`, `Aidatlar`, and `Hesaplar`. This is a deliberate least-privilege design (these roles are meant to see dashboards/reports only, not raw collections/ledger/bank data). `SearchController.Global` bypasses that design: any authenticated user in the `Personel` or `SadeceGoruntuleme` role — who has no explicit permission to view Collections, Ledger, or Cash/Bank data — can still retrieve it through the global search endpoint, including:
  - Collection (Tahsilat) records: amount, date, receipt/reference number, payer unit and owner name.
  - Ledger transactions: description, amount, category, and linked bank account name/branch/**IBAN**.
  - Bank accounts: name, branch, **IBAN** (`x.Iban`).
  - Cash boxes and income/expense categories.
- **Impact**: Vertical/cross-module privilege escalation — a low-privilege role (report-viewer/personnel) gains read access to sensitive financial records (payment history, ledger entries, bank IBANs) that the permission matrix explicitly withholds from that role. This is a confidentiality breach of financial data (bank IBANs, transaction amounts/descriptions, payer PII) reachable simply by supplying a search term that matches existing records — or, for `Collections`/`LedgerTransactions`/`BankAccounts`/`CashBoxes`/categories, simply supplying a numeric ID to hit `hasNumericId` matches, enabling ID enumeration of financial rows without needing to guess a text term.
- **Proof**:
  ```csharp
  [ModuleAuthorize(AppModules.Raporlar)]
  public class SearchController(ApplicationDbContext db) : Controller
  {
      public async Task<IActionResult> Global(string? term)
      {
          ...
          var collections = await db.Collections.AsNoTracking()
              .Include(x => x.BankAccount)
              .Include(x => x.CashBox)
              .Where(x => (hasNumericId && x.Id == numericId) || ... )
              ...
          var ledgerRows = await db.LedgerTransactions.AsNoTracking()
              .Include(x => x.BankAccount)
              .Where(x => ... || (x.BankAccount != null && x.BankAccount.Iban != null && x.BankAccount.Iban.ToLower().Contains(normalized)))
              ...
          var bankAccounts = await db.BankAccounts.AsNoTracking()
              .Where(x => (hasNumericId && x.Id == numericId) || ... || (x.Iban != null && x.Iban.ToLower().Contains(normalized)))
              ...
  ```
  No per-entity-type permission check exists anywhere in the action body — only the blanket class-level `Raporlar` check.
- **Remediation**: Gate each result block behind the permission for the module it belongs to, e.g. only include `collections` results if `permissions.HasAccess(user, AppModules.Tahsilatlar, write:false)`; only include `ledgerRows`/`bankAccounts`/`cashBoxes` if the caller has `KasaBanka`/`Muhasebe` view; only include `units`/`accounts` if caller has `Daireler`/`Hesaplar` view. Inject `PermissionService` into the controller and filter each `results.AddRange(...)` call behind the corresponding module check before executing/returning it.
- **Dynamic Test**:
  ```
  1. Create or use a test user assigned only the `Personel` role (seeded with view-only access to `Panel` + `Raporlar`, no `Tahsilatlar`/`Muhasebe`/`KasaBanka`).
  2. Log in as that user, confirm navigating to `/Collections` or `/CashBank` desktop pages is denied (403) due to lacking module permission.
  3. Issue `GET /Search/Global?term=<any digits matching an existing Collection/LedgerTransaction/BankAccount Id>` (e.g., `term=1`) or a term matching an existing IBAN/reference number/description.
  4. Observe the JSON response includes "Tahsilat #...", "Finans #...", and bank/cash entries with amounts, IBANs, and reference numbers — data the user has no explicit module permission to view, confirming the authorization bypass.
  ```

---

## Not Vulnerable (verified, no action required)

### AccountsController — general CRUD
- **File**: `Controllers/AccountsController.cs` (lines 11-16 class decl; actions throughout)
- **Endpoint**: `GET/POST /Accounts/{Index,Search,Detail,Create,Edit,AddUnitAccess,RemoveUnitAccess,Delete}`
- **Protection**: Class-level `[ModuleAuthorize(AppModules.Hesaplar)]`. Verified against `Services/ModuleAuthorizeAttribute.cs` and the seeded `RolePermissions` matrix in `Program.cs` — only `SiteYonetici`/`SistemYonetici` reach write actions; no action opts out.

### AccountsController.Edit (GET) — plaintext resident PIN in server-side view model
- **File**: `Controllers/AccountsController.cs` (lines 304-315, 435-484, esp. line 479); `Views/Accounts/Edit.cshtml` (lines 94-124)
- **Endpoint**: `GET /Accounts/Edit/{id}`
- **Protection**: `BuildEditPageAsync` unconditionally sets `MobilePassword = account.MobilePassword` on the C# view model regardless of caller's role, but the Razor view wraps the entire "Mobil Giriş Bilgileri" block referencing `@Model.MobilePassword` in a server-side `@if (Model.CanManageLogin)` check (`CanManageLogin = User.IsInRole(AppRoles.SistemYonetici)`). Razor renders server-side before the response is sent, so when `CanManageLogin` is false the block — including the PIN — is never emitted to the client. No JSON/API variant serializes the full view model. **Not exploitable as implemented**, though it is a code-hygiene concern: populating a sensitive field on the model unconditionally is fragile (a future refactor, AJAX variant, or debug dump could leak it). Recommend (optional hardening, not a required fix): set `MobilePassword` to `null` unless `CanManageLogin` is true, matching the code comment's stated intent end-to-end.

### AccountsController.ResetMobilePassword
- **File**: `Controllers/AccountsController.cs` (lines 393-404)
- **Endpoint**: `POST /Accounts/ResetMobilePassword`
- **Protection**: `[Authorize(Policy = AppPolicies.SystemAdmin)]` action-level override, stricter than class-level `[ModuleAuthorize(AppModules.Hesaplar)]`.

### ReportsController — general reports
- **File**: `Controllers/ReportsController.cs` (lines 12-1276)
- **Endpoint**: `GET/POST /Reports/*` (reports, installment edit/delete, balance-detailed line/manual-entry CRUD)
- **Protection**: Class-level `[ModuleAuthorize(AppModules.Raporlar)]`; every action verified, no `[AllowAnonymous]`, all mutating actions POST + antiforgery-token protected.

### ReportsController.LoginCredentials / LoginCredentialDetail / LoginCredentialsExcel
- **File**: `Controllers/ReportsController.cs` (lines 26-78)
- **Endpoint**: `GET /Reports/LoginCredentials{,Detail,Excel}`
- **Protection**: `[Authorize(Policy = AppPolicies.SystemAdmin)]` action-level, conjunctive with class-level `Raporlar` check — correctly gates the bulk plaintext PIN/username export behind the strongest role check in the app.

### BackupsController — full DB backup/restore
- **File**: `Controllers/BackupsController.cs` (lines 8-83)
- **Endpoint**: `GET /Backups/Index`, `POST /Backups/Create`, `GET /Backups/Download?fileName=`, `POST /Backups/Restore`
- **Protection**: Class-level `[Authorize(Policy = AppPolicies.SystemAdmin)]` applies uniformly to all 4 actions, including `Download` (no per-action attribute needed — inherits class policy). Note: `Download`'s `fileName` handling should be separately verified by the path-traversal check (out of scope here).

### SystemUsersController — staff user/role management
- **File**: `Controllers/SystemUsersController.cs` (lines 12-266)
- **Endpoint**: `GET/POST /SystemUsers/{Index,Create,Edit}`
- **Protection**: Class-level `[Authorize(Policy = AppPolicies.SystemAdmin)]`, no per-action overrides.

### RolePermissionsController — the permission matrix itself
- **File**: `Controllers/RolePermissionsController.cs` (lines 10-100)
- **Endpoint**: `GET/POST /RolePermissions/Index`
- **Protection**: Class-level `[Authorize(Policy = AppPolicies.SystemAdmin)]`, no per-action overrides. `SistemYonetici` is also hardcoded as always fully privileged and excluded from the editable matrix, so a compromised matrix entry can't de-privilege the admin role itself.

### AuditController — audit log, soft-delete restore, import rollback, allocation repair
- **File**: `Controllers/AuditController.cs` (lines 11-539)
- **Endpoint**: `GET /Audit/Index`, `POST /Audit/{Restore,RollbackImport,RunConsistencyCheck,RepairCollectionAllocation,RepairAllCollectionAllocations}`, `GET /Audit/ImportErrorsCsv`
- **Protection**: Class-level `[Authorize(Policy = AppPolicies.SystemAdmin)]`, all 7 actions verified individually, no overrides. Internal helper methods (`RestoreEntityAsync<T>`) are private, not independently reachable.

### SettingsController
- **File**: `Controllers/SettingsController.cs` (lines 7-11)
- **Endpoint**: `GET /Settings/Index`
- **Protection**: Class-level `[Authorize(Policy = AppPolicies.SystemAdmin)]`; empty view, no state-changing logic.

### LedgerController / OpeningBalancesController / CollectionsController / CashBankController / IncomeExpenseCategoriesController
- **Files**: `Controllers/LedgerController.cs`, `OpeningBalancesController.cs`, `CollectionsController.cs`, `CashBankController.cs`, `IncomeExpenseCategoriesController.cs`
- **Endpoint**: full CRUD + CSV import/export under `/Ledger`, `/OpeningBalances`, `/Collections`, `/CashBank`, `/IncomeExpenseCategories`
- **Protection**: `[ModuleAuthorize(AppModules.Muhasebe)]` / `[ModuleAuthorize(AppModules.Tahsilatlar)]` / `[ModuleAuthorize(AppModules.KasaBanka)]` respectively, applied class-level; every mutating action across all 5 files confirmed `[HttpPost][ValidateAntiForgeryToken]`, no `[AllowAnonymous]` anywhere.

### DuesController / DuesTypesController / DuesGenerationController / BillingGroupsController
- **Files**: `Controllers/DuesController.cs`, `DuesTypesController.cs`, `DuesGenerationController.cs`, `BillingGroupsController.cs`
- **Endpoint**: dues installment CRUD, bulk dues generation/deletion by period, billing-group CRUD
- **Protection**: All `[ModuleAuthorize(AppModules.Aidatlar)]` class-level. `DuesGenerationController.Generate`/`Delete` (bulk-destructive, period-wide) confirmed POST-only and correctly requiring write permission via the class filter.

### UnitsController / BlocksController
- **Files**: `Controllers/UnitsController.cs` (lines 11-1215), `Controllers/BlocksController.cs` (lines 10-113)
- **Endpoint**: unit/block CRUD, bulk delete/combine, CSV import/export
- **Protection**: `[ModuleAuthorize(AppModules.Daireler)]` class-level on both; bulk-impact actions (`DeleteSelected`, `CombineSelected`, `ImportCsv`) are POST-only with no unguarded GET counterpart.

### DocumentsController — file attachment CRUD/download
- **File**: `Controllers/DocumentsController.cs` (lines 10-189)
- **Endpoint**: `GET/POST /Documents/{Index,Create,Details,Edit,PreviewFile,DownloadFile,DeleteAttachment,Delete}`
- **Protection**: `[ModuleAuthorize(AppModules.Belgeler)]` class-level; `PreviewFile`/`DownloadFile` (raw file serving) fully inherit the class filter, no bypass attribute. (Note: attachment lookup scoping is an IDOR concern, out of scope for this skill.)

### AnnouncementsController / RequestsController
- **Files**: `Controllers/AnnouncementsController.cs` (lines 11-109), `Controllers/RequestsController.cs` (lines 12-172)
- **Endpoint**: CRUD under `/Announcements`, `/Requests`
- **Protection**: `[ModuleAuthorize(AppModules.Duyurular)]` / `[ModuleAuthorize(AppModules.Talepler)]` class-level; all mutating actions POST + antiforgery-protected.

### HomeController (Dashboard)
- **File**: `Controllers/HomeController.cs`
- **Endpoint**: `GET /Home/{Index,Error}`
- **Protection**: Class-level `[ModuleAuthorize(AppModules.Panel)]`; aggregate/rollup data only.

### Mobile Area Controllers (Panel, Daireler, Duyurular, Talepler, Gider, Raporlar, KasaBanka)
- **Files**: `Areas/Mobile/Controllers/{Panel,Daireler,Duyurular,Talepler,Gider,Raporlar,KasaBanka}Controller.cs`
- **Endpoint**: `GET/POST /m/{controller}/...`
- **Protection**: Each carries class-level `[ModuleAuthorize(AppModules.X)]` matching its domain (Gider → `Muhasebe`); grep-confirmed no `[AllowAnonymous]` anywhere in the 7 files, including write actions (`Talepler.Yeni/Guncelle`, `Gider.Yeni/Duzenle/EkSil/MahsupSil`). Layered on top of `MobileScopeService` unit-scoping (IDOR concern, separate skill).

### Mobile HesapController — own-account profile / PIN change
- **File**: `Areas/Mobile/Controllers/HesapController.cs` (lines 9-51)
- **Endpoint**: `GET /m/Hesap/Diger`, `GET/POST /m/Hesap/Sifre`
- **Protection**: Plain `[Authorize]`, correct since the action resolves the target exclusively via `userManager.GetUserAsync(User)` — no cross-user parameter exists to authorize against.

### Mobile BildirimlerController — push subscription management
- **File**: `Areas/Mobile/Controllers/BildirimlerController.cs` (lines 11-138)
- **Endpoint**: `GET /m/Bildirimler/Index`, `POST /m/Bildirimler/{Abone,AbonelikSil,TumunuOku}`, `GET /m/Bildirimler/{Ozet,Ac}`
- **Protection**: Plain `[Authorize]`; identity derived from `CurrentUserId` claim, not client input. (Note: `AbonelikSil(string endpoint)` deletes a `PushSubscription` matched only by `endpoint` with no `UserId == CurrentUserId` check before removal — potential IDOR, flagged for the IDOR skill, not assessed further here as it is authentication-present / out of this skill's scope.)

### Mobile YardimController — static help page
- **File**: `Areas/Mobile/Controllers/YardimController.cs` (lines 6-12)
- **Endpoint**: `GET /m/Yardim/Kurulum`
- **Protection**: Plain `[Authorize]`; static content, no data access, intentionally role-agnostic per code comment.

### Identity area — Login page
- **File**: `Areas/Identity/Pages/Account/Login.cshtml.cs`
- **Endpoint**: `GET/POST /Identity/Account/Login`
- **Protection**: Intentionally anonymous (standard Identity UI convention); only authenticates existing users, does not create accounts or elevate privilege.

### Identity area — Register page (admin-only self-registration lock)
- **File**: `Program.cs` line 111 (`options.Conventions.AuthorizeAreaPage("Identity", "/Account/Register", AppPolicies.SystemAdmin)`); page itself served from the default Identity UI Razor Class Library (no local override)
- **Endpoint**: `GET/POST /Identity/Account/Register`
- **Protection**: Convention-based authorization correctly scoped to `SistemYonetici` role, confirmed no conflicting `AuthorizeFolder`/`AllowAnonymousToPage` convention exists, and no local page override with `[AllowAnonymous]` exists to take precedence. Self-registration is not publicly reachable.

### Identity area — Manage/* pages (self-service profile/security pages)
- **Files**: `Areas/Identity/Pages/Account/Manage/*.cshtml.cs`
- **Endpoint**: `GET/POST /Identity/Account/Manage/*`
- **Protection**: No `[AllowAnonymous]` anywhere under `Areas/Identity` (grep-confirmed); relies on framework-default `AuthorizeFolder` convention. Every page resolves the user via `userManager.GetUserAsync(User)` only — no admin-style id parameter on any page.

### ResidentAccountService auto-provisioning (`EnsureLoginAsync`)
- **File**: `Services/ResidentAccountService.cs` (lines 28-65)
- **Endpoint**: N/A — not directly HTTP-reachable; triggered only from `AccountsController` (`[ModuleAuthorize(AppModules.Hesaplar)]`), `SystemUsersController` (`[Authorize(Policy = AppPolicies.SystemAdmin)]`), or `Program.cs` startup seeding (`SeedResidentAccountsAsync`, internal-only).
- **Protection**: No unauthenticated or user-reachable account-creation/registration bypass exists.
