# Kumburgaz MCP Bağlayıcısı — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Kumburgaz'ı claude.ai'ye URL ile eklenen, OAuth ile giriş yapılan, kullanıcının yetkileriyle okuma ve onaylı kayıt yapan bir MCP bağlayıcısı haline getirmek.

**Architecture:** Aynı ASP.NET Core uygulamasına iki parça eklenir:
- OpenIddict 7 ile yetkilendirme sunucusu: authorization code + PKCE, refresh token, Claude dönüş adreslerine kısıtlı DCR.
- Resmi C# MCP SDK ile `/mcp` ucu: stateless Streamable HTTP.

Araçlar mevcut servisleri kullanır. Yazma mantığı önce servislere taşınır (gelir/gider, tahsilat dağıtım planı); hem ekran hem MCP aynı kodu çağırır.

**Tech Stack:** .NET 10, ASP.NET Core MVC + Identity, EF Core 10 (Npgsql / testte SQLite), OpenIddict 7.9.0, ModelContextProtocol.AspNetCore 2.4.0, xUnit + WebApplicationFactory.

**Spec:** `docs/superpowers/specs/2026-11-22-mcp-connector-design.md`

## Global Constraints

- MCP adresi: `https://kumburgaz.bahceliler.net/mcp`. Issuer yapılandırması `OAuth:Issuer`, varsayılan `https://kumburgaz.bahceliler.net/`. Testler `OAuth__Issuer=https://localhost/` verir ve istemciyi `BaseAddress = https://localhost` ile kurar.
- DCR'de izinli dönüş adresleri, birebir eşleşme: `https://claude.ai/api/mcp/auth_callback`, `https://claude.com/api/mcp/auth_callback`.
- Kapsam (scope): `mcp`. PKCE zorunlu (`S256`). Access token 1 saat, refresh token 30 gün (kayan).
- Bağlayıcıyı yalnız şu rollerden en az birine sahip, kilitli olmayan kullanıcılar kullanır: `SistemYonetici`, `SiteYonetici`, `MuhasebeGorevli`, `Personel`, `SadeceGoruntuleme`. `Sakin` rolü veya rolsüz kullanıcı reddedilir.
- Her araç `PermissionService.HasAccess(user, module, needWrite)` ile modül kontrolü yapar. Okuma `needWrite: false`, yazma `true`.
- Silme ve düzenleme aracı yok. Yazma araçları yalnız `record_collection`, `create_expense`, `create_income`.
- Yazma araçları `confirm: false` iken veritabanına yazmaz. `confirm: true` için `idempotency_key` zorunludur (8–100 karakter); anahtar kullanıcı + araç bazında 24 saat saklanır.
- Tutar girdisi sayı ya da Türkçe metin olabilir; `FlexibleDecimalParser` ile okunur ve sıfırdan büyük olmalıdır. Tarih girdisi `YYYY-AA-GG`; verilmezse bugün.
- Liste araçlarında `limit` varsayılan 100, en fazla 500.
- Araç sonuçları `link` alanında issuer tabanlı mutlak uygulama adresi taşır.
- Token'daki görünen ad `"{FullName ?? UserName} (Claude)"` olur. Böylece AuditLog'daki `UserName` MCP kaynaklı kayıtları ayırt eder; şema değişmez.
- Yeni tablolar tek migration'da: `AddOAuthAndMcp`.

## Review Focus

1. **`resource` parametresi.** claude.ai authorize/token isteklerinde RFC 8707 `resource=https://…/mcp` gönderir. İstek reddedilmemeli, token'ın audience'ı bu adres olmalı. Test: Task 5.
2. **Onaydan sonra değişen kullanıcı.** Rolü Sakin'e çevrilen veya kilitlenen kullanıcının refresh token'ı `invalid_grant` almalı; yeni token eski yetkileri taşımamalı. Test: Task 5.
3. **Ağ yeniden denemesi.** Aynı `idempotency_key` ile iki `confirm: true` çağrısı tek kayıt açmalı ve ikinci çağrı ilk sonucu dönmeli. Farklı anahtar iki kayıt açar. Test: Task 10.
4. **Türkçe arama.** `"şahin"`, `"SAHIN"`, `"Şahİn"` aynı kişiyi bulmalı. `"A-5"`, `"a blok 5"`, `"A5"` aynı daireyi bulmalı. Telefon boşluklu/boşluksuz eşleşmeli. Test: Task 8.
5. **Önizleme ile kayıt arasında veri değişirse.** Önizlemeden sonra başka bir tahsilat girildiyse `confirm: true` güncel planla yazmalı ve sonuçta gerçekten yazılan dağıtımı dönmeli, bayat önizlemeyi değil. Test: Task 10.

---

### Task 1: Gelir/gider kaydını servise taşı

**Files:**
- Create: `Services/LedgerEntryService.cs`
- Modify: `Controllers/LedgerController.cs` (`Create` POST, ~satır 118-148), `Program.cs` (DI kaydı)
- Test: `tests/Kumburgaz.Web.Tests/LedgerEntryServiceTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum LedgerEntryKind { Income, Expense }
  public sealed record LedgerEntryRequest(DateTime Date, int CategoryId, decimal Amount, string? AccountKey, string? Description);
  public sealed record LedgerEntryPreview(LedgerEntryKind Kind, int CategoryId, string CategoryName, decimal Amount, DateTime Date, string AccountKey, string AccountName, string? Description);
  public class LedgerEntryService(ApplicationDbContext db)
  {
      Task<LedgerEntryPreview> PreviewAsync(LedgerEntryRequest request, LedgerEntryKind? expectedKind = null);
      Task<int> CreateAsync(LedgerEntryRequest request, LedgerEntryKind? expectedKind = null); // LedgerTransaction.Id
  }
  ```
  Kategori tipi `CategoryTypeHelper.Normalize(category.Type)` ile okunur: Gelir → Income, diğer → Expense. Hata durumunda `InvalidOperationException` atılır; Türkçe mesajlar:
  - `"Kategori bulunamadı."`
  - `"Seçilen kategori bir gider kategorisi değil."` / `"... gelir kategorisi değil."`
  - `"Geçerli bir kasa/banka hesabı seçiniz."` (anahtar `FinancialAccountHelper.TryParse` ile çözülemez ya da hesap yok/pasif)
  - `"Tutar sıfırdan büyük olmalı."`

Kapsam notu (spec'ten sapma): `CashBankController`'daki `LedgerTransactions.Add` çağrıları transfer, açılış ve mahsup kayıtlarıdır; düz gelir/gider değildir. MCP bunlara dokunmadığı için yerinde kalırlar.

- [ ] **Step 1: Failing testleri yaz.** InMemory DB; mevcut testlerdeki `CreateDb()` kalıbını kullan.
  - `CreateAsync_writes_expense_with_resolved_bank_account`: `bank:{id}` anahtarı → `PaymentChannel.Bank`, `BankAccountId == id`, `CashBoxId == null`, tarih UTC.
  - `PreviewAsync_does_not_write`: önizlemeden sonra `db.LedgerTransactions.Count() == 0`; `AccountName` ve `CategoryName` dolu.
  - `CreateAsync_rejects_income_category_when_expense_expected` → `InvalidOperationException`.
  - `CreateAsync_rejects_unknown_account_key` (`"bank:999"`) → `InvalidOperationException`.
- [ ] **Step 2:** `dotnet test --filter LedgerEntryServiceTests` → derleme hatası (tip yok).
- [ ] **Step 3:** Servisi yaz ve `builder.Services.AddScoped<LedgerEntryService>()` ile kaydet. `LedgerController.Create` POST `entity` oluşturma bloğunu `await ledgerEntryService.CreateAsync(new(model.Date, model.IncomeExpenseCategoryId, model.Amount, model.AccountKey, model.Description))` ile değiştir. `InvalidOperationException` gelirse `ModelState.AddModelError(string.Empty, ex.Message)` ve formu yeniden göster. Ek dosya kaydı aynı kalır. Geçersiz anahtarın sessizce hesapsız kaydedilmesi bu değişiklikle doğrulama hatasına döner; bu bilinçli bir davranış değişikliğidir.
- [ ] **Step 4:** `dotnet test` → hepsi geçer.
- [ ] **Step 5:** Commit: `refactor: gelir/gider kaydını LedgerEntryService'e taşı`

### Task 2: Tahsilat dağıtım planını ayrı metoda çıkar

**Files:**
- Modify: `Services/ICollectionService.cs`, `Services/CollectionService.cs` (`SaveCollectionAndReallocateAsync`, ~satır 94-280), `Controllers/CollectionsController.cs` (`CreateForUnit`, ~satır 80-160)
- Test: `tests/Kumburgaz.Web.Tests/CollectionAllocationPlanTests.cs`

**Interfaces:**
- Produces (`ICollectionService` üyeleri):
  ```csharp
  public sealed record AllocationLine(int? DuesInstallmentId, string Label, decimal Applied, decimal RemainingAfter); // Label: "Devir borcu" ya da "{Period} {DuesTypeName}"
  public sealed record CollectionAllocationPlan(int BillingGroupId, int UnitId, IReadOnlyList<AllocationLine> Lines, decimal Advance);
  Task<CollectionAllocationPlan> PlanAllocationAsync(CollectionCreateViewModel model); // salt okunur
  Task<CollectionCreateViewModel> BuildForUnitAsync(int unitId, decimal amount, DateTime date, string? accountKey,
      string? period, bool isReceipt, string? referenceNo, string? note);
  ```
  - `BuildForUnitAsync`, `CreateForUnit`'teki aidat grubu çözümünü ve dönem/devir/en eski taksit seçimini taşır.
  - Aktif aidat grubu yoksa `InvalidOperationException("Bu daire için aktif bir aidat grubu bulunamadı.")` atar.
  - `CreateForUnit` artık bu metodu çağırır.

- [ ] **Step 1: Failing testleri yaz.**
  - `Plan_matches_allocations_written_by_CreateAsync`: devir -300, iki açık taksit (500, 500), tahsilat 1000. Plan satırları ve `CreateAsync` sonrası `CollectionAllocations` aynı olmalı: devir 300, taksit1 500, taksit2 200, `Advance == 0`.
  - `Plan_with_target_installment_keeps_overflow_as_advance`: tek taksite 700 TL; satır 500, `Advance == 200`.
  - `Plan_does_not_write`: plan sonrası `ChangeTracker.HasChanges() == false` ve tahsisat sayısı değişmez.
  - `BuildForUnitAsync_devir_period_targets_no_installment`: `period: "devir"` → `DuesInstallmentId == null`.
- [ ] **Step 2:** Testler derlenmez/kırmızı.
- [ ] **Step 3:** Mevcut sorgu ve dağıtım döngüsünü `PlanAllocationAsync` içine taşı (`AsNoTracking`, aynı `OrderBy(Period).ThenBy(DueDate).ThenBy(Id)`). `SaveCollectionAndReallocateAsync` hem yeni kayıt hem düzenleme yolunda planı bu metotla hesaplasın ve satırları uygulasın. Düzenleme yolunda geri alma ve `SaveChangesAsync` plandan önce kalır.
- [ ] **Step 4:** `dotnet test` → yeni testler ve mevcut `UnitLedgerAndCollectionTests` geçer.
- [ ] **Step 5:** Commit: `refactor: tahsilat dağıtım planını PlanAllocationAsync'e çıkar`

### Task 3: OpenIddict temeli, anahtar deposu ve metadata

**Files:**
- Create:
  - `Auth/OAuthConstants.cs`
  - `Auth/OAuthServerSetup.cs` (`AddKumburgazOAuthServer`)
  - `Auth/OAuthKeyStore.cs`
  - `Models/OAuthModels.cs` (`OAuthKey`, `McpIdempotencyKey`)
  - `Data/Migrations/*_AddOAuthAndMcp.cs`
- Modify: `Kumburgaz.Web.csproj`, `Data/ApplicationDbContext.cs`, `Program.cs`
- Test: `tests/Kumburgaz.Web.Tests/Integration/OAuthMetadataTests.cs`, `tests/Kumburgaz.Web.Tests/OAuthKeyStoreTests.cs`, `tests/Kumburgaz.Web.Tests/Integration/KumburgazAppFactory.cs`

**Interfaces:**
- Produces:
  ```csharp
  public static class OAuthConstants
  {
      public const string Scope = "mcp";
      public const string McpPolicy = "Mcp";
      public static readonly IReadOnlySet<string> AllowedRedirectUris; // Global Constraints'teki iki adres
      public static readonly IReadOnlySet<string> EligibleRoles;       // Global Constraints'teki beş rol
      public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);
      public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
  }
  public class OAuthKey { int Id; string Use /* "sig" | "enc" */; string ProtectedPrivateKeyPem; DateTime CreatedAt; }
  public class McpIdempotencyKey { string UserId; string Tool; string Key; string ResultJson; DateTime CreatedAt; } // PK (UserId, Tool, Key)
  public static IServiceCollection AddKumburgazOAuthServer(this IServiceCollection services, IConfiguration config);
  public static Uri GetOAuthIssuer(this IConfiguration config); // OAuth:Issuer, sonu "/" ile
  ```
- Packages: `OpenIddict.AspNetCore` 7.9.0, `OpenIddict.EntityFrameworkCore` 7.9.0.
- Yapılandırma:
  - DbContext `options.UseOpenIddict()`.
  - Server: `SetIssuer(issuer)`, `SetAuthorizationEndpointUris("connect/authorize")`, `SetTokenEndpointUris("connect/token")`, `AllowAuthorizationCodeFlow().AllowRefreshTokenFlow()`, `RequireProofKeyForCodeExchange()`, `RegisterScopes(OAuthConstants.Scope)`, ömürler, `UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough()`.
  - `HandleConfigurationRequestContext` inline handler: `context.Metadata["registration_endpoint"] = new Uri(issuer, "connect/register").AbsoluteUri`.
  - Validation: `UseLocalServer()`, `UseAspNetCore()`, `AddAudiences(new Uri(issuer, "mcp").AbsoluteUri)`.
  - Anahtarlar: `OAuthKeyStore` bir `IConfigureOptions<OpenIddictServerOptions>` olarak `SigningCredentials` (RS256) ve `EncryptionCredentials` (RSA-OAEP + A256CBC-HS512) ekler. RSA 2048 anahtarlar `OAuthKeys` tablosunda `IDataProtector("Kumburgaz.OAuthKeys")` ile korunmuş PEM olarak durur; yoksa üretilip kaydedilir. Böylece deploy'lar arasında token'lar geçerli kalır.

- [ ] **Step 1: Failing testleri yaz.**
  - `KumburgazAppFactory` ctor'una `OAuth__Issuer=https://localhost/` ortam değişkenini ekle (önceki değeri geri yükleyerek). İstemci `BaseAddress = https://localhost` ile kurulsun.
  - `Authorization_server_metadata_advertises_code_flow_pkce_and_registration`: `GET /.well-known/oauth-authorization-server` → 200; `issuer == "https://localhost/"`, `authorization_endpoint` `/connect/authorize` ile, `token_endpoint` `/connect/token` ile, `registration_endpoint` `/connect/register` ile biter; `code_challenge_methods_supported` `"S256"` içerir; `scopes_supported` `"mcp"` içerir.
  - `OAuthKeyStoreTests.Keys_are_created_once_and_reused`: aynı InMemory DB ve `EphemeralDataProtectionProvider` ile iki kez yükle → aynı `KeyId`, tabloda 2 satır.
- [ ] **Step 2:** Testler kırmızı.
- [ ] **Step 3:** Paketleri ekle ve yapılandırmayı yaz. `dotnet ef migrations add AddOAuthAndMcp` (Npgsql) çalıştır. `dotnet ef migrations has-pending-model-changes` → değişiklik yok.
- [ ] **Step 4:** `dotnet test` → hepsi geçer (SQLite EnsureCreated yeni tabloları kurar).
- [ ] **Step 5:** Commit: `feat: OpenIddict yetkilendirme sunucusu temeli ve kalıcı anahtarlar`

### Task 4: Dinamik istemci kaydı (DCR)

**Files:**
- Create: `Auth/ClientRegistrationController.cs`
- Modify: `Program.cs` (rate limiter + `app.UseRateLimiter()`)
- Test: `tests/Kumburgaz.Web.Tests/Integration/ClientRegistrationTests.cs`

**Interfaces:**
- Produces: `POST /connect/register`, JSON gövde (RFC 7591, snake_case):
  - İstek: `client_name`, `redirect_uris`, `grant_types`, `response_types`, `token_endpoint_auth_method`.
  - Yanıt 201: `client_id`, `client_id_issued_at`, `client_name`, `redirect_uris`, `grant_types: ["authorization_code","refresh_token"]`, `response_types: ["code"]`, `token_endpoint_auth_method`; confidential istemcide ek olarak `client_secret`.
  - Hata 400: `{ "error": "invalid_redirect_uri" | "invalid_client_metadata", "error_description": "…" }`.
  - `[AllowAnonymous]`, `[IgnoreAntiforgeryToken]`, `[EnableRateLimiting("oauth-register")]`.
- İstemci kaydı `IOpenIddictApplicationManager.CreateAsync` ile yapılır:
  - `ConsentType = Explicit`
  - `token_endpoint_auth_method == "none"` (varsayılan) → `ClientType = Public`; `"client_secret_post"` veya `"client_secret_basic"` → `Confidential` + 32 baytlık rastgele secret
  - İzinler: authorization ve token uçları, authorization_code ve refresh_token grant'ları, code response type, `scp:mcp`
  - Gereksinim: `Features.ProofKeyForCodeExchange`
- Rate limit: sabit pencere, saatte 20; bölüm anahtarı `CF-Connecting-IP` başlığı, yoksa `RemoteIpAddress`.

- [ ] **Step 1: Failing testleri yaz.**
  - `Registers_public_client_for_claude_callback`: 201, `client_id` dolu, `token_endpoint_auth_method == "none"`, `client_secret` yok.
  - `Registers_confidential_client_when_secret_auth_requested`: `client_secret_post` → `client_secret` dolu.
  - `Rejects_redirect_uri_outside_allowlist`: `https://evil.example/cb` → 400 `invalid_redirect_uri`.
  - `Rejects_missing_redirect_uris` → 400 `invalid_client_metadata`.
  - `Limits_registrations_per_ip`: aynı `CF-Connecting-IP` ile 21. istek → 429.
- [ ] **Step 2:** Kırmızı.
- [ ] **Step 3:** Controller'ı ve limiter'ı yaz.
- [ ] **Step 4:** `dotnet test` → geçer.
- [ ] **Step 5:** Commit: `feat: Claude dönüş adreslerine kısıtlı dinamik istemci kaydı`

### Task 5: Yetkilendirme, onay ekranı ve token ucu

**Files:**
- Create: `Auth/AuthorizationController.cs`, `Auth/OAuthPrincipalFactory.cs`, `Views/Authorization/Consent.cshtml`
- Test: `tests/Kumburgaz.Web.Tests/Integration/OAuthTestClient.cs`, `tests/Kumburgaz.Web.Tests/Integration/AuthorizationFlowTests.cs`

**Interfaces:**
- Consumes: Task 3'teki `OAuthConstants`; Task 4'teki `POST /connect/register`.
- Produces:
  ```csharp
  public class OAuthPrincipalFactory(UserManager<ApplicationUser> users)
  {
      // Uygun değilse null: rolsüz, yalnız Sakin, kilitli.
      Task<ClaimsPrincipal?> CreateAsync(ApplicationUser user, ImmutableArray<string> scopes, string? resource);
  }
  ```
  Principal'ın claim'leri:
  - `sub` ve `ClaimTypes.NameIdentifier` = user.Id
  - `name` = UserName
  - `ApplicationUserClaimsPrincipalFactory.DisplayNameClaimType` = `"{FullName ?? UserName} (Claude)"`
  - her rol için `role`
  - Identity name/role claim tipleri `Claims.Name` / `Claims.Role`; tüm claim'lerin hedefi AccessToken.
  - `SetScopes(scopes)`, `SetResources(resource ?? issuer + "mcp")`.

  Uç davranışları:
  - `GET /connect/authorize`:
    - Cookie oturumu yoksa `Challenge(IdentityConstants.ApplicationScheme)` ile mevcut Login'e gider ve geri döner.
    - Kullanıcı uygun değilse `Forbid` (`access_denied`, açıklama "Bu hesap bağlayıcıyı kullanamaz.").
    - Aynı istemci ve kapsam için kalıcı (permanent) authorization varsa doğrudan `SignIn`.
    - Yoksa `Consent` görünümü: istemci adı ve "Claude, sizin yetkilerinizle verileri okuyabilecek ve sizin onayınızla tahsilat/gelir/gider kaydı açabilecek." metni.
  - `POST /connect/authorize` (`[ValidateAntiForgeryToken]`): `submit=accept` → permanent authorization oluştur ve `SignIn`; `submit=deny` → `Forbid` (`access_denied`).
  - `POST /connect/token`: code veya refresh grant'ında OpenIddict server şemasıyla authenticate et, `sub` ile kullanıcıyı yeniden yükle, `OAuthPrincipalFactory` null dönerse `Forbid` (`invalid_grant`), değilse yeni principal ile `SignIn`.
- Test yardımcısı `OAuthTestClient(KumburgazAppFactory factory)`:
  - `Task<ApplicationUser> CreateUserAsync(string userName, string password, params string[] roles)`
  - `Task<TokenResult> GetTokensAsync(string userName, string password, string? resource = null)`: DCR → Login sayfası (`Input.UserNameOrEmail`, `Input.Password`, antiforgery token HTML'den) → authorize (PKCE S256) → onay POST → dönüş adresinden `code` → token
  - `Task<HttpResponseMessage> RefreshAsync(string clientId, string refreshToken)`
  - `record TokenResult(string ClientId, string AccessToken, string RefreshToken, int ExpiresIn)`

- [ ] **Step 1: Failing testleri yaz.**
  - `Staff_user_completes_code_flow_and_gets_tokens`: `MuhasebeGorevli` kullanıcı → `AccessToken` ve `RefreshToken` dolu, `ExpiresIn == 3600`.
  - `Resource_parameter_is_accepted` (Review Focus 1): `resource: "https://localhost/mcp"` ile akış başarılı.
  - `Sakin_user_is_denied`: authorize sonrası dönüş adresinde `error=access_denied`.
  - `Token_request_without_code_verifier_fails` → 400.
  - `Refresh_fails_after_user_loses_staff_role` (Review Focus 2): token al, rolleri `Sakin` yap, refresh → 400 `invalid_grant`.
  - `Second_authorization_skips_consent`: ikinci akışta onay POST'u olmadan code döner.
- [ ] **Step 2:** Kırmızı.
- [ ] **Step 3:** Controller'ı, principal fabrikasını ve onay görünümünü yaz. Görünüm mevcut Login sayfasının stilinde olsun.
- [ ] **Step 4:** `dotnet test` → geçer.
- [ ] **Step 5:** Commit: `feat: OAuth yetkilendirme ve token uçları, onay ekranı`

### Task 6: Bağlı uygulamalar ekranı

**Files:**
- Create: `Controllers/ConnectedAppsController.cs`, `Views/ConnectedApps/Index.cshtml`
- Modify: `Views/Shared/_Layout.cshtml` (Ayarlar bağlantısının yanına "Bağlı uygulamalar"; Settings sayfasında da bağlantı)
- Test: `tests/Kumburgaz.Web.Tests/Integration/ConnectedAppsTests.cs`

**Interfaces:**
- Consumes: Task 5'teki `OAuthTestClient`.
- Produces:
  - `[Authorize] GET /ConnectedApps`: kullanıcının authorization'ları (istemci adı, oluşturulma tarihi, durum). `SistemYonetici` herkesinkini kullanıcı adıyla görür.
  - `POST /ConnectedApps/Revoke/{id}` (`[ValidateAntiForgeryToken]`): `IOpenIddictAuthorizationManager.TryRevokeAsync` ve `IOpenIddictTokenManager.RevokeByAuthorizationIdAsync`. Başkasının kaydını iptal etmeye çalışan yönetici olmayan kullanıcı 404 alır.

- [ ] **Step 1: Failing testleri yaz.**
  - `Revoked_connection_cannot_refresh`: iptal sonrası refresh → 400.
  - `User_cannot_revoke_someone_elses_connection` → 404.
  - `Index_lists_own_connection`: sayfa istemci adını içerir.
- [ ] **Step 2:** Kırmızı. **Step 3:** Yaz. **Step 4:** `dotnet test` → geçer.
- [ ] **Step 5:** Commit: `feat: bağlı uygulamalar ekranı ve bağlantı iptali`

### Task 7: `/mcp` ucu, yetki, talimatlar ve rehber

**Files:**
- Create: `Mcp/McpSetup.cs`, `Mcp/McpInstructions.cs`, `Mcp/McpGuideResource.cs`, `Mcp/McpAccess.cs`, `Mcp/McpInput.cs`, `Mcp/McpLinks.cs`, `Mcp/Tools/CategoryTools.cs`
- Modify: `Kumburgaz.Web.csproj` (`ModelContextProtocol.AspNetCore` 2.4.0), `Program.cs`
- Test: `tests/Kumburgaz.Web.Tests/Integration/McpEndpointTests.cs`, `tests/Kumburgaz.Web.Tests/McpInputTests.cs`; test projesine `ModelContextProtocol` 2.4.0

**Interfaces:**
- Consumes: Task 5'teki `OAuthTestClient.GetTokensAsync`; Task 3'teki `GetOAuthIssuer()`.
- Produces:
  ```csharp
  public static IServiceCollection AddKumburgazMcp(this IServiceCollection services, IConfiguration config);
  public static void MapKumburgazMcp(this WebApplication app); // app.MapMcp("/mcp").RequireAuthorization(OAuthConstants.McpPolicy)
  public static class McpAccess { static void Require(ClaimsPrincipal user, PermissionService permissions, string module, bool write); } // McpException: "Bu işlem için yetkiniz yok ({module})."
  public static class McpInput
  {
      static decimal ParseAmount(JsonElement amount); // sayı ya da metin; <= 0 veya okunamaz → McpException("Geçerli bir tutar giriniz.")
      static DateTime ParseDate(string? value);       // null → bugün; "YYYY-AA-GG" dışı → McpException
      static int ClampLimit(int? limit);              // null → 100; 1..500
  }
  public sealed class McpLinks(IConfiguration config) { string Unit(int id); string Collection(int id); string Ledger(int id); string CashBank(string accountKey); }
  [McpServerToolType] public class CategoryTools { /* categories(kind?: "income"|"expense") → [{ id, name, kind }] */ }
  ```
- Kimlik doğrulama:
  - `AddAuthentication().AddMcp(o => { o.ForwardAuthenticate = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme; o.ResourceMetadata = new() { Resource = new Uri(issuer, "mcp"), AuthorizationServers = { issuer.AbsoluteUri }, ScopesSupported = ["mcp"], ResourceName = "Kumburgaz" }; })`. Varsayılan şemalar (Identity cookie) değişmez.
  - `McpPolicy`: `AddAuthenticationSchemes(McpAuthenticationDefaults.AuthenticationScheme)`, `RequireAuthenticatedUser()`, scope `mcp` kontrolü (OpenIddict `HasScope`).
- Sunucu: `AddMcpServer(o => { o.ServerInfo = new() { Name = "Kumburgaz", Version = "1.0" }; o.ServerInstructions = McpInstructions.Text; }).WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless).AddAuthorizationFilters().WithTools<CategoryTools>().WithResources<McpGuideResource>()`. Sonraki task'lar `WithTools<…>` ekler.
- `McpInstructions.Text` (birebir):
  ```
  Kumburgaz: site yönetimi (aidat, tahsilat, kasa/banka, gelir/gider). Bu bağlantı oturum açan kullanıcının yetkileriyle çalışır.
  - Kullanıcıya Türkçe yanıt ver. Para: ₺2.500,00 biçiminde; tarih: GG.AA.YYYY. Araçlar tarihleri YYYY-AA-GG, tutarları sayı olarak verir.
  - Daire ya da kişi adı geçince önce "search" ile kimliği bul, sonra get_unit / unit_statement çağır. Birden çok eşleşme varsa tahmin etme, kullanıcıya sor.
  - "Kim ödemedi?" → overdue_dues; "bu ay ne tahsil edildi?" → collections (from/to); genel durum → site_summary; kasa/banka → cash_bank_accounts.
  - Yanıtlarda ilgili kaydın "link" adresini ver.
  - Kayıt araçlarında (record_collection, create_expense, create_income) önce confirm: false ile önizle, önizlemeyi kullanıcıya göster, açık onayından sonra aynı girdilerle confirm: true çağır. confirm: true için her işleme yeni bir idempotency_key (UUID) gönder; tekrar denerken aynısını kullan.
  - Silme ve düzenleme bu bağlantıyla yapılamaz; kullanıcıyı uygulamadaki "link"e yönlendir.
  - Kavramlar için "kumburgaz://rehber" kaynağını oku.
  ```
- `McpGuideResource`: `kumburgaz://rehber`, `text/markdown`. Aidat grubu, birleşik daire, devir bakiyesi, avans, mahsup, kasa/banka anahtarları (`cash:{id}` / `bank:{id}`) ve dönem biçimi kısa paragraflarla anlatılır.

- [ ] **Step 1: Failing testleri yaz.**
  - `Mcp_without_token_returns_401_with_resource_metadata`: `POST /mcp` → 401; `WWW-Authenticate` `resource_metadata="https://localhost/.well-known/oauth-protected-resource` içerir.
  - `Protected_resource_metadata_points_to_this_server`: `GET /.well-known/oauth-protected-resource` → `authorization_servers` `"https://localhost/"` içerir.
  - `Authenticated_client_lists_tools_and_reads_instructions`: `HttpClientTransport` (`AdditionalHeaders["Authorization"] = "Bearer …"`, TestServer `HttpClient`) → `McpClient.CreateAsync`; `ListToolsAsync` `categories` içerir; `client.ServerInstructions` `"₺2.500,00"` içerir.
  - `Tool_rejects_user_without_module_permission`: Muhasebe görüntüleme yetkisi olmayan rol (yetki matrisinden kaldır) → `categories` çağrısı `IsError == true`, metin `"yetkiniz yok"` içerir.
  - `McpInputTests`: `ParseAmount` için `2500` (sayı) → 2500, `"2.500,00"` → 2500, `"-5"` ve `"abc"` → `McpException`; `ClampLimit(null) == 100`, `ClampLimit(9999) == 500`.
- [ ] **Step 2:** Kırmızı. **Step 3:** Yaz. **Step 4:** `dotnet test` → geçer.
- [ ] **Step 5:** Commit: `feat: OAuth korumalı /mcp ucu, sunucu talimatları ve rehber`

### Task 8: Okuma araçları — daireler ve borçlar

**Files:**
- Create: `Mcp/Tools/UnitTools.cs`, `Services/TurkishSearchNormalizer.cs`
- Modify: `Mcp/McpSetup.cs` (`WithTools<UnitTools>()`)
- Test: `tests/Kumburgaz.Web.Tests/McpUnitToolsTests.cs`, `tests/Kumburgaz.Web.Tests/TurkishSearchNormalizerTests.cs`

**Interfaces:**
- Consumes: Task 7'deki `McpAccess`, `McpInput`, `McpLinks`.
- Produces (araç adları ve dönüş alanları; hepsi `ReadOnly = true`):
  - `search(query)`, modül Daireler. En fazla 20 sonuç: `[{ type: "unit"|"account", id, title, subtitle, link }]`. Daire no, blok adı, malik/kiracı adı, telefon eşleşir.
  - `get_unit(unitId)`, modül Daireler. `{ id, display, block, people: [{ name, type, phone }], billingGroup, balance, devirRemaining, openInstallments: [{ id, period, dueDate, amount, remaining }], recentCollections: [{ id, date, amount, account }], link }`. En fazla 5 son tahsilat. Kaynak `UnitLedgerService.BuildAsync`.
  - `unit_statement(unitId, from?, to?)`, modül Daireler. `{ unit, entries: [{ date, kind, description, amount, runningBalance }], closingBalance }`. Kaynak `UnitStatementService.BuildAsync`, tarih aralığıyla süzülür.
  - `overdue_dues(period?, blockId?, limit?)`, modül Raporlar. `{ total, rows: [{ unitId, unit, responsible, remaining, oldestPeriod, link }] }`. Kalanı sıfırdan büyük olanlar, en çok borçlu önce. Kaynak `IReportingService.GetDuesDebtReportAsync`.
  - `static string TurkishSearchNormalizer.Normalize(string? value)`: küçük harf, Türkçe harfler ASCII'ye (ı/İ→i, ş→s, ğ→g, ü→u, ö→o, ç→c), harf/rakam dışı karakterler atılır, boşluklar silinir; `"blok"` kelimesi düşer ki `"a blok 5"` ile `"A-5"` aynı anahtara insin.

- [ ] **Step 1: Failing testleri yaz.**
  - `TurkishSearchNormalizerTests` (Review Focus 4): `"Şahİn"`, `"SAHIN"`, `"şahin"` → `"sahin"`; `"A-5"`, `"a blok 5"`, `"A5"` → `"a5"`; `"0532 111 22 33"` ile `"05321112233"` eşit.
  - `Search_finds_unit_by_block_and_number_variants`, `Search_finds_owner_by_name_without_turkish_letters`.
  - `Get_unit_returns_open_installments_and_link`: link `https://localhost/Units/Detail/{id}` ile biter.
  - `Overdue_dues_orders_by_remaining_desc_and_respects_limit`.
  - `Unit_statement_filters_by_date_range`.
- [ ] **Step 2:** Kırmızı. **Step 3:** Yaz. **Step 4:** `dotnet test` → geçer.
- [ ] **Step 5:** Commit: `feat: MCP daire, arama ve borç araçları`

### Task 9: Okuma araçları — özet, tahsilat, kasa/banka, defter

**Files:**
- Create: `Mcp/Tools/FinanceTools.cs`
- Modify: `Mcp/McpSetup.cs`
- Test: `tests/Kumburgaz.Web.Tests/McpFinanceToolsTests.cs`

**Interfaces:**
- Consumes: Task 7 yardımcıları.
- Produces (hepsi `ReadOnly = true`):
  - `site_summary()`, modül Panel. `{ cashTotal, bankTotal, accounts: [{ key, name, balance }], duesDebtTotal, overdueDebtTotal, collectionsThisMonth, expensesThisMonth, incomeThisMonth }`.
    - Kaynaklar: `CashBankBalanceHelper.BuildAsync` ve `DuesDebtSummaryHelper.Build(GetDuesDebtReportAsync(new()))`.
    - Ay toplamları `Collections` ve `LedgerTransactions` üzerinden; Türkiye saatine göre ayın 1'i ile bugün arası.
    - Değerler Panel ekranıyla aynı olmalı.
  - `collections(from?, to?, unitId?, accountKey?, limit?)`, modül Tahsilatlar. `{ total, rows: [{ id, date, unit, amount, account, referenceNo, isReceipt, link }] }`. En yeni önce.
  - `cash_bank_accounts()`, modül KasaBanka. `[{ key, type, name, balance, link }]`.
  - `cash_bank_transactions(accountKey, from?, to?, limit?)`, modül KasaBanka. `{ account, opening, closing, rows: [{ date, description, inflow, outflow, balance }] }`. Kaynak `CashBankDetailService.BuildAsync`.
  - `ledger(kind?: "income"|"expense", categoryId?, from?, to?, limit?)`, modül Muhasebe. `{ total, rows: [{ id, date, kind, category, amount, account, description, link }] }`.

- [ ] **Step 1: Failing testleri yaz.**
  - `Site_summary_matches_cash_bank_balances`: toplamlar `CashBankBalanceHelper.BuildAsync` sonucuyla aynı.
  - `Collections_filters_by_date_and_orders_newest_first`.
  - `Cash_bank_transactions_closing_equals_account_balance`.
  - `Ledger_filters_expenses_by_category`.
- [ ] **Step 2:** Kırmızı. **Step 3:** Yaz. **Step 4:** `dotnet test` → geçer.
- [ ] **Step 5:** Commit: `feat: MCP özet, tahsilat, kasa/banka ve defter araçları`

### Task 10: Yazma araçları ve tekrar koruması

**Files:**
- Create: `Mcp/McpIdempotencyStore.cs`, `Mcp/Tools/WriteTools.cs`
- Modify: `Mcp/McpSetup.cs`
- Test: `tests/Kumburgaz.Web.Tests/McpWriteToolsTests.cs`, `tests/Kumburgaz.Web.Tests/Integration/McpWriteFlowTests.cs`

**Interfaces:**
- Consumes:
  - Task 1: `LedgerEntryService.PreviewAsync/CreateAsync`
  - Task 2: `ICollectionService.BuildForUnitAsync`, `PlanAllocationAsync`, `CreateAsync`
  - Task 3: `McpIdempotencyKey` tablosu
  - Task 7 yardımcıları
- Produces:
  ```csharp
  public class McpIdempotencyStore(ApplicationDbContext db)
  {
      Task<string?> TryGetAsync(string userId, string tool, string key); // 24 saatten eskiyse null
      Task SaveAsync(string userId, string tool, string key, string resultJson); // kaydederken 24 saatten eski satırları siler
  }
  ```
  Araçlar (`Destructive = false`, `Idempotent = false`):
  - `record_collection(unitId, amount, accountKey, date?, period?, isReceipt = false, referenceNo?, note?, confirm = false, idempotency_key?)`, modül Tahsilatlar.
  - `create_expense(categoryId, amount, accountKey, description, date?, confirm = false, idempotency_key?)`, modül Muhasebe.
  - `create_income(...)`: `create_expense` ile aynı imza, kategori gelir olmalı.

  Yanıtlar:
  - Önizleme: `{ willWrite: false, summary, unit?, account, amount, date, lines?: [{ label, applied, remainingAfter }], advance?, category? }`.
  - Kayıt: `{ created: true, id, link, lines?, advance? }`.
  - `confirm: true` ve `idempotency_key` yok → `McpException("confirm: true için idempotency_key gönderin.")`.
  - `isReceipt: true` ve `referenceNo` yok → `McpException("Makbuz için makbuz no giriniz.")`.
  - Kayıt yolu planı yeniden hesaplar (`CreateAsync` içindeki planlayıcı) ve gerçekten yazılan tahsisatları `CollectionAllocations` tablosundan okuyup döner.

- [ ] **Step 1: Failing testleri yaz.**
  - `Record_collection_preview_writes_nothing`: `Collections` ve `CollectionAllocations` sayıları değişmez; `lines` planla aynı.
  - `Record_collection_confirm_creates_once_per_idempotency_key` (Review Focus 3): aynı anahtarla iki çağrı → 1 tahsilat, ikinci yanıtın `id`'si aynı; farklı anahtar → 2 tahsilat.
  - `Record_collection_confirm_uses_current_state_not_stale_preview` (Review Focus 5): önizleme al, araya başka bir tahsilat ekle, confirm → dönen `lines` güncel planla aynı.
  - `Confirm_without_idempotency_key_is_rejected`.
  - `Create_expense_rejects_income_category`.
  - `Write_tool_requires_write_permission`: `SadeceGoruntuleme` → hata.
  - Entegrasyon `McpWriteFlowTests.Collection_created_via_mcp_is_audited_as_claude`: MCP istemcisiyle confirm → `AuditLogs` satırında `UserName` `"(Claude)"` ile biter.
- [ ] **Step 2:** Kırmızı. **Step 3:** Yaz. **Step 4:** `dotnet test` → geçer.
- [ ] **Step 5:** Commit: `feat: MCP tahsilat/gelir/gider kayıt araçları ve tekrar koruması`

### Task 11: Canlıya alma ve claude.ai doğrulaması

**Files:**
- Modify: `docs/superpowers/specs/2026-11-22-mcp-connector-design.md` (durum: uygulandı; kullanım notu)

- [ ] **Step 1:** PR aç, CI'ın (build + test) geçtiğini gör, merge et, Coolify deploy'unu başlat ve `finished` durumunu bekle.
- [ ] **Step 2: Canlı smoke testi.**
  - `curl -s https://kumburgaz.bahceliler.net/.well-known/oauth-authorization-server` → issuer `https://kumburgaz.bahceliler.net/`, `registration_endpoint` var.
  - `curl -si -X POST https://kumburgaz.bahceliler.net/mcp` → 401 ve `resource_metadata`.
  - `curl -s https://kumburgaz.bahceliler.net/.well-known/oauth-protected-resource` → `authorization_servers` doğru.
- [ ] **Step 3: Kullanıcı adımı.** claude.ai → Ayarlar → Bağlayıcılar → Özel bağlayıcı ekle → `https://kumburgaz.bahceliler.net/mcp` → giriş → onay. Ardından "Genel durum nedir?" (`site_summary`), "A blok 5'in borcu?" (`search` → `get_unit`) ve bir tahsilat önizlemesi (`confirm: false`) denenir.
- [ ] **Step 4:** Commit: `docs: MCP bağlayıcısı kullanım notu`
