# Architecture: Kumburgaz (Site/Apartment Management Web App)

## Technology Stack

| Category | Details |
|---|---|
| Languages | C# (.NET 10, target `net10.0`, preview/rc packages) |
| Frameworks | ASP.NET Core MVC (Controllers + Views), Razor Pages (ASP.NET Core Identity UI area), EF Core 10 (rc) |
| Databases | Dual provider: SQLite (`Microsoft.Data.Sqlite`, dev/local — `app 2.db` present in repo root) or PostgreSQL (`Npgsql.EntityFrameworkCore.PostgreSQL`, prod) selected at runtime by connection-string shape |
| Auth mechanism | ASP.NET Core Identity (`AddDefaultIdentity<ApplicationUser>`) with cookie auth, role-based (`IdentityRole`), custom module-permission matrix (`RolePermission` table + `PermissionService` + `[ModuleAuthorize]` filter), plus a second "Sakin" (resident) mobile-only auth path using account-id-as-username + 5-digit numeric PIN |
| Infrastructure | Dockerfile, nixpacks.toml (Railway/Nixpacks style deploy), single-container monolith, `PORT` env var driven |
| External services | Web Push (`Lib.Net.Http.WebPush`, VAPID keys in config), QuestPDF (PDF report generation), ClosedXML (Excel export), SixLabors.ImageSharp (image resize/compress), `pg_dump`/`pg_restore` external binaries shelled out for Postgres backup/restore |

## Architecture Overview

Single ASP.NET Core monolith serving three surfaces:

1. **Desktop/admin area** — `Controllers/*` (site management: units, accounts, dues, collections, ledger, cash/bank, reports, documents, announcements, requests, users, role permissions, backups, audit, settings). Gated by role + a custom per-module CanView/CanWrite matrix stored in `RolePermissions` table, enforced via `[ModuleAuthorize(AppModules.X)]` class-level filter (`Services/ModuleAuthorizeAttribute.cs`) which maps HTTP verb → view/write requirement.
2. **Mobile/resident area** — `Areas/Mobile/Controllers/*`, routed under `/m`, intended for unit owners/tenants ("Sakin" role) to view their own dues, requests, announcements, cash/bank (limited), and submit expense-mahsup entries. Scoping to "my units only" is enforced by `Services/MobileScopeService.cs` (union of owned `UnitAccounts` + granted `AccountUnitAccesses`).
3. **Identity area** — `Areas/Identity/Pages/Account/*` (Razor Pages, standard ASP.NET Identity UI: login, manage account, 2FA, change password, delete/download personal data). Self-registration page is locked to `SystemAdmin` policy only (`Program.cs` `AuthorizeAreaPage("Identity", "/Account/Register", AppPolicies.SystemAdmin)`).

Cross-cutting: `SakinAreaRestrictionFilter` (global MVC filter) blocks the "Sakin" role from any controller outside `Mobile`/`Identity` areas (redirect on GET, 403 on write) — a defense-in-depth boundary so desktop controllers don't need per-endpoint resident checks, though several desktop controllers layer their own `[ModuleAuthorize]` as well.

Background/hosted services: `BackupHostedService` (scheduled DB backup), `ConsistencyCheckHostedService` (periodic data-integrity audit), `PushDispatchHostedService` (drains `PushQueue` for web-push notifications).

## Data Flow

- **Auth (staff)**: Identity Razor Pages → `SignInManager`/cookie → role + module-permission claims resolved per-request from `RolePermissions` table (30-min memory cache in `PermissionService`).
- **Auth (resident/mobile)**: `ResidentAccountService` auto-provisions an `ApplicationUser` per Owner/Tenant `Account` on seed, username = account Id (stringified), password = a 5-digit numeric PIN. The PIN is stored **both** as the Identity password hash and in plaintext in `Account.MobilePassword` "so admins can view/reset it" (explicit design choice per code comments) — exposed via a "User Credentials" report (`GetCredentialsAsync`) presumably restricted to admin roles.
- **Core business data**: user input in controller actions (units, accounts, dues, collections, ledger entries, cash/bank, mahsup/offset transactions) → EF Core (parameterized LINQ, no raw SQL found) → SQLite/Postgres.
- **File uploads**: `DocumentFileService` (generic document attachments, extension+content-type allowlist, 25MB cap, content buffered in DB as `byte[]`) and `ImageAttachmentService` (receipt/expense photos, re-encoded via ImageSharp to strip original bytes, resized, JPEG re-compressed). CSV import (`CsvImportHelper`) for bulk data entry; CSV/Excel export (`CsvExportHelper`, ClosedXML) and PDF export (QuestPDF, `BalanceDetailedReportService` etc.) for reports.
- **Backups**: `BackupService` shells out to `pg_dump`/`pg_restore` (or file-copies the SQLite file) under `BackupsController` (`SystemAdmin`-only). Connection-string components (host/port/db/user) are interpolated into the process argument string; password passed via `PGPASSWORD` env var (not on command line). Tool path configurable via config/env var.
- **Push notifications**: `NotificationService`/`PushSenderService` enqueue via `PushQueue` (in-memory singleton), dispatched by `PushDispatchHostedService` to Web Push endpoints using VAPID keys from config.

## Entry Points

| Entry Point | Type | Auth Required | Description |
|---|---|---|---|
| `/Identity/Account/Login` etc. | Razor Pages | No (login itself) | Standard Identity auth pages |
| `/{controller}/{action}/{id?}` (desktop) | MVC | Yes, role+module (`[ModuleAuthorize]`) or `SystemAdmin` policy | ~22 desktop controllers: Accounts, Units, Blocks, Dues*, Collections, Ledger, CashBank, IncomeExpenseCategories, OpeningBalances, Documents, Announcements, Requests, Reports, Search, Backups, SystemUsers, RolePermissions, Audit, Settings, Home |
| `/m/{controller}/{action}/{id?}` (mobile) | MVC (Area) | Yes (any authenticated; scoped to own units via `MobileScopeService`) | Panel, Daireler (units), Hesap (account/password), Duyurular (announcements), Talepler (requests), Bildirimler (notifications), KasaBanka, Gider (expenses/mahsup), Raporlar, Yardim |
| Root `/` UA-based redirect | Middleware | N/A | Mobile user-agents auto-redirected to `/m` before routing |
| Hosted services | Background | N/A (no external trigger) | Backup, consistency check, push dispatch timers |

## Trust Boundaries

- **Browser → server**: all controller action parameters (route/query/form/JSON-bound models) — primary untrusted input surface. File uploads are a distinct sub-boundary (content-type/extension spoofing risk).
- **Resident (Sakin) → server**: lower-privilege authenticated boundary; must not reach desktop controllers or other residents' units — enforced by `SakinAreaRestrictionFilter` + `MobileScopeService`.
- **Server → database**: EF Core parameterized queries (SQLite or Postgres) — no raw SQL construction observed in application code.
- **Server → OS process**: `BackupService` invokes `pg_dump`/`pg_restore` with connection-string-derived arguments (host/port/db/user come from `appsettings.json`/env, not directly from end users, but worth verifying no user-controlled path into connection string or `reason`/backup filename parameters).
- **Server → filesystem**: backup file listing/restore (`ResolveBackupPath` sanitizes via `Path.GetFileName`), document/image attachments (stored as DB blobs, not filesystem — reduces path traversal surface for those).
- **Server → browser (output)**: Razor views (auto-encoding by default), PDF/Excel/CSV export generation from stored data.

## Sensitive Data Inventory

| Data Type | Where Stored | How Accessed | Protection |
|---|---|---|---|
| Staff credentials | ASP.NET Identity tables (hashed) | Identity UI | Standard Identity hashing |
| Resident PIN (mobile login) | `Account.MobilePassword` (plaintext) + Identity hash | `ResidentAccountService.GetCredentialsAsync` ("User Credentials" report), admin PIN reset flow | **Plaintext PIN stored by design** — sensitive, needs auth check verification on who can view the report |
| Financial data (dues, collections, ledger, cash/bank balances) | Postgres/SQLite via EF models | Desktop controllers (Muhasebe/Aidatlar/KasaBanka modules) + Mobile (own-unit scope) | Role/module permission matrix + mobile unit-scoping |
| Personal data (resident name, phone, unit) | `Account`, `Unit` tables | Various controllers/reports | Module permissions; Identity "Download/Delete personal data" pages present |
| Documents/attachments (potentially PII-bearing files) | DB `byte[]` blobs (`Attachment`) | `DocumentsController`, mobile Gider (expense receipts) | Content-type/extension allowlist, size cap, image re-encoding |
| DB backups (full data dumps) | Filesystem (`Backups:Directory`), downloadable | `BackupsController` (`SystemAdmin` only) | Policy-gated; filename sanitized via `Path.GetFileName` |
| Data Protection keys (used to encrypt auth cookies) | DB (`PersistKeysToDbContext`) | Framework-internal | Persisted to survive container redeploys |
| Push VAPID keys | `appsettings.json` (`Push:PrivateKey`, currently blank placeholders) | `PushSenderService` | Config-based secret — check for hardcoding in real deployment |

## Notable Areas for Deeper Review

- `ResidentAccountService` / `RolePermissionsController` / reports exposing `MobilePassword`: verify who can reach the credentials report and reset endpoints (IDOR/missing-auth risk given PIN is account-id-based username).
- `BackupService`: process argument construction (potential command-injection if `Backups:PgDumpPath`/config becomes attacker-influenced) and backup file restore flow.
- `DocumentFileService`/`ImageAttachmentService`: extension/content-type allowlist bypass, and whether uploaded content is ever written to disk/served with attacker-controlled filename elsewhere (e.g., export features).
- `SakinAreaRestrictionFilter` + `MobileScopeService`: correctness of unit-scoping across all Mobile controllers (IDOR check on unit/account IDs passed as route/query params).
- `ModuleAuthorizeAttribute`: verb-based view/write inference — confirm no controller exposes a state-changing GET action that would be under-protected.
- CSV import (`CsvImportHelper`) and ImportBatch flow: injection via crafted CSV content (formula injection in exports, parsing issues).
