# Kumburgaz.Web — Kapsamlı Code Review

**Tarih:** 2026-07-03
**Yöntem:** 4 paralel review ajanı (Controllers, Services, Data/Config, Views/JS), bulgular dedupe edilip severity'ye göre sıralandı. Her bulgu ilgili kod okunarak doğrulandı; spekülatif bulgular elendi.

**Ön notlar:**
- Working tree'deki uncommitted değişiklikler (`FlexibleDecimalParser.cs`, `CashBankDetailService.cs`, `CashBankController.cs`, migration dosyaları) **tamamen satır sonu (CRLF↔LF) değişikliği** — `git diff -w` boş. Semantik değişiklik yok. Öneri: `.gitattributes`'a `* text=auto` ekleyin ki gerçek diff'ler okunabilir kalsın.
- `dotnet build` doğrulanamadı (makinede .NET SDK yok); statik incelemede derleme kırıcı bulunmadı.

**Özet:** 4 CRITICAL, 8 HIGH, 27 MEDIUM, 18 LOW bulgu. En acil tema: **kimlik doğrulama/yetkilendirme fiilen yok** (anonim kayıt + rol kontrolü yok + hardcoded admin) ve **tutar parse etme yollarının tutarsızlığı** finansal verileri sessizce bozabiliyor (×100/×1000 hatalar).

---

## CRITICAL

### C1. Anonim self-registration açık, rol kontrolü hiçbir yerde yok
- **Yer:** `Program.cs:32-41` (`AddDefaultIdentity`, `RequireConfirmedAccount = false`), `Program.cs:105` (`MapRazorPages`), tüm `Controllers/*` (çıplak `[Authorize]`, hiçbirinde `Roles=` yok)
- **Sorun:** `AddDefaultIdentity` paketlenmiş Identity Default UI'ı getiriyor; `Areas/Identity`'de sadece Login/Logout scaffold edilmiş ama `/Identity/Account/Register` framework'ten servis edilmeye devam ediyor. Roller (`SistemYonetici` vs.) `Program.cs:128`'de seed ediliyor ama **hiçbir controller'da zorlanmıyor**.
- **Senaryo:** Siteye erişebilen herkes `/Identity/Account/Register`'dan hesap açar (6 karakter şifre, e-posta onayı yok) ve anında tüm aidat, tahsilat, kasa/banka ve yevmiye kayıtlarına tam okuma/yazma yetkisi kazanır.
- **Fix:** `Register.cshtml.cs`'i scaffold edip `NotFound()` döndürün (veya Default UI'ı tamamen çıkarıp `AddIdentity<ApplicationUser, IdentityRole>()` kullanın), finansal controller'lara `[Authorize(Roles = "...")]` veya policy ekleyin.

### C2. Kaynak koda gömülü admin şifresi
- **Yer:** `Program.cs:137-150`
- **Sorun:** `admin@kumburgaz.local` / `"Admin123!"` her ortamda her başlangıçta seed ediliyor.
- **Senaryo:** Repo'yu gören herkes (veya bu bilinen seed'i tahmin eden biri) şifresi hiç değiştirilmemiş herhangi bir deployment'ta SistemYonetici olarak giriş yapar.
- **Fix:** Seed şifresini env/user-secrets'tan okuyun (`builder.Configuration["Seed:AdminPassword"]`), tanımsızsa seed'i atlayın/başarısız edin; ilk girişte şifre değişikliği zorlayın.

### C3. `FlexibleDecimalParser`: "2.500" → 2,50 TL (1000× sessiz hata)
- **Yer:** `Services/FlexibleDecimalParser.cs:34-42`
- **Sorun:** Tek nokta + 3 basamak (`2.500`) ondalık nokta sayılıyor; oysa çoklu nokta dalı (`1.234.567`) aynı `.ddd` ekini binlik ayracı olarak işliyor. Türkçe bağlamda tutarsız ve yanlış.
- **Senaryo:** Banka CSV'sinde veya formda `2.500` (= 2.500 TL) → invariant parse → **2,50 TL** kaydedilir. Import önizlemesi ham metni (`2.500`) gösterdiği için kullanıcıya doğru görünür; `CommitImport` 2,50 TL işler. Sessiz, sınırsız para kaybı.
- **Fix:** Tek nokta + tam 3 ondalık basamak durumunu binlik ayracı say (çoklu nokta dalıyla aynı kural):
  ```csharp
  else if (dotIndex >= 0)
  {
      var decimals = normalized.Length - dotIndex - 1;
      if (normalized.IndexOf('.') != dotIndex) // çoklu nokta — mevcut mantık
          normalized = ...;
      else if (decimals == 3) // "2.500" → "2500"
          normalized = normalized.Replace(".", string.Empty);
  }
  ```
  (`1.5`, `12.34` davranışı değişmez; sadece 3 basamaklı durum düzelir.)

### C4. Tahsilat düzenlemesi, tamamen ödenmiş taksitlerin allocation'larını sessizce siler
- **Yer:** `Services/CollectionService.cs:135-149` (bellek içi rollback) vs `:180-193` (DB'ye giden yeniden sorgu)
- **Sorun:** Eski allocation'lar **sadece bellekte** geri alınıyor (SaveChanges yok), sonra açık taksitler `Where(x => x.RemainingAmount > 0)` ile **DB'deki bayat değerlere karşı** SQL'de yeniden sorgulanıyor — bu tahsilatın tam ödediği taksitler filtreden düşüyor.
- **Senaryo:** 500 TL'lik tahsilat A taksitini tam öder (DB'de Remaining=0). Kullanıcı tahsilatı düzenler (sadece not değişikliği bile yeter — `CashBankController.UpdateCollectionTransaction` → `UpdateAsync`). Rollback A.Remaining=500'ü bellekte yapar; SQL filtresi A'yı dışlar. Para grubun *sonraki* taksitlerine dağıtılır veya avansa dönüşür; A `Open`/ödenmemiş olarak kaydedilir. Hedeflenen `DuesInstallmentId` kendi ödemesini alamaz.
- **Fix:** Rollback'i filtreye görünür kılın:
  ```csharp
  var rolledBackIds = collection.Allocations.Select(a => a.DuesInstallmentId).ToList();
  var openInstallmentsQuery = db.DuesInstallments
      .Where(x => x.BillingGroupId == billingGroupId
               && (x.RemainingAmount > 0 || rolledBackIds.Contains(x.Id)));
  ```
  veya rollback sonrası (mevcut transaction içinde) `SaveChangesAsync()` çağırın.

---

## HIGH

### H1. `HomeController`'da `[Authorize]` yok — finansal dashboard anonim erişime açık
- **Yer:** `Controllers/HomeController.cs:10` (18 controller'dan tek `[Authorize]`'suz olanı); `Program.cs`'de fallback policy yok
- **Senaryo:** Anonim ziyaretçi `/` isteğinde tüm kasa/banka bakiyelerini, gecikmiş borçluları (isim + tutar), tahsilat toplamlarını görür. Uygulama `0.0.0.0:$PORT`'a bağlanıyor, yani public deployment için yazılmış.
- **Fix:** Sınıfa `[Authorize]` (+ `Error()`'a `[AllowAnonymous]`), veya daha iyisi global fallback: `SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())`.

### H2. CSV import'ta tutar parse'ı tr-TR öncelikli — uygulamanın kendi export→import döngüsü tutarları ×100 şişirir
- **Yer:** `Controllers/CollectionsController.cs:478-482`, `Controllers/LedgerController.cs:426-430` (`TryParseAmount` önce tr-TR dener); export'lar InvariantCulture yazar: `CollectionsController.cs:35`, `LedgerController.cs:50,94`
- **Senaryo:** `ExportCsv` `"2500.75"` yazar. Aynı dosya import edilince `decimal.TryParse("2500.75", tr-TR)` noktayı binlik ayracı sayar → **250075** kaydedilir. Sessiz 100× şişme; taksitler aşırı-allocate edilir.
- **Fix:** Her iki import yolunda `FlexibleDecimalParser.TryParse` kullanın (C3 fix'i sonrası), veya export'u tr-TR formatına çevirin.

### H3. `asp-for` ile bağlanan tutar alanları tr-TR altında nokta ondalığı sessizce yanlış parse ediyor
- **Yer:** `Views/Collections/Create.cshtml:41`, `Collections/Edit.cshtml:41`, `Ledger/Create.cshtml:35`, `Ledger/Edit.cshtml:32`, `Reports/EditInstallment.cshtml:76`, `Dues/CreateInstallment.cshtml:63`, `DuesTypes/Create.cshtml:25`, `DuesTypes/Edit.cshtml:25`, `Units/Create.cshtml:69`, `Units/Edit.cshtml:71`; kök neden: `wwwroot/js/site.js:16-34`
- **Sorun:** `site.js`, jQuery-validate'in `number` metodunu hem virgül hem noktayı kabul edecek şekilde patch'liyor; ama server tarafı model binding tr-TR ile çalışıyor (`.` = binlik ayracı).
- **Senaryo:** Kullanıcı `1234.56` yazar → client validation geçer → server **123456** bind eder. Tutar/bakiye sessizce 100× şişer. CashBank formları `FlexibleDecimalParser` kullandığı için aynı tutar ekrana göre farklı parse ediliyor.
- **Fix:** Bu aksiyonlarda tutarı `Request.Form[...]` + `FlexibleDecimalParser` ile okuyun (CashBank'taki `TryReadFormDecimal` deseni), veya client validator'ı server'ın kabul etmeyeceği girdiyi reddedecek şekilde daraltın.

### H4. `Replace(',', '.')` + invariant parse — Units CSV import'u açılış bakiyesini sessizce sıfırlıyor
- **Yer:** `Controllers/UnitsController.cs:1110-1118` (`ParseDecimal` — hata yerine `0m` döner), `Controllers/CollectionsController.cs:84-85`, `Controllers/OpeningBalancesController.cs:57-60`
- **Senaryo:** `1.234` (Türkçe binlik, = 1234 TL) → **1,234 TL** kaydedilir. `1.234,56` → `"1.234.56"` → parse başarısız: Collections'da hata mesajı; **UnitsController'da sessizce `0`** — dairenin import edilen açılış bakiyesi silinir.
- **Fix:** Üç yerde de `FlexibleDecimalParser.TryParse` kullanın; `UnitsController.ImportCsv`'de parse edilemeyen bakiyeyi `0` değil satır hatası yapın.

### H5. `CreateForUnit` tahsilatı yanlış daireye kaydedip başka dairelerin borcunu ödüyor
- **Yer:** `Controllers/CollectionsController.cs:78-124`; kök neden `Services/CollectionService.cs:119, 180-193`
- **Senaryo:** A dairesinin açık taksiti yok ama merged olmayan bir fatura grubunu B, C ile paylaşıyor. A'nın detay sayfasından "Tahsilat ekle" → `targetInstallment` null → `collection.UnitId = ResolveRepresentativeUnitIdAsync(group)` (alfabetik ilk daire, A değil!) ve allocation döngüsü A'nın parasını FIFO ile **B ve C'nin** taksitlerine uygular. A'nın ekstresinde hiçbir şey görünmez; B, A'nın parasıyla ödenmiş görünür.
- **Fix:** `CollectionCreateViewModel`'e `UnitId` ekleyin; `SaveCollectionAndReallocateAsync` verildiğinde `openInstallmentsQuery.Where(x => x.UnitId == model.UnitId)` filtrelesin ve `collection.UnitId = model.UnitId` set etsin.

### H6. Transfer sınıflandırması Index ile Detail sayfası arasında farklı — aynı hesap iki ekranda farklı bakiye gösterebilir
- **Yer:** `Services/CashBankDetailService.cs:278-291` (isim/açıklama heuristiği), `:339-347` (`SignedLedgerAmount`); karşılaştırma: `CashBankController` Index bakiyesi sadece `x.IsTransfer` flag'ine bakıyor
- **Senaryo:** Kategori adı "Transfer" içeren bir gelir kategorisi (örn. "Havale/Transfer Gelirleri") veya açıklaması "Para transferi:" ile başlayan gelir satırı: Index'te **+tutar**, Detail'de (ve CSV export'ta) **−tutar** — iki ekran tutarın 2 katı kadar çelişir. `20260701165925_MakeLedgerTransfersCategoryless` migration'ı `IsTransfer`/`TransferIsIncoming`'i zaten backfill etti; heuristik artık sadece yanlış tetiklenebilir.
- **Fix:** İsim/açıklama heuristiğini kaldırıp `tx.IsTransfer` flag'ine güvenin.

### H7. Finansal kayıtlarda convention'dan gelen cascade delete
- **Yer:** `Data/ApplicationDbContext.cs` (bu FK'ler için config yok); snapshot doğrulaması: `ApplicationDbContextModelSnapshot.cs:1170-1184` (Collection→BillingGroup, Collection→Unit Cascade), `:1124-1127` (BillingGroup→DuesType), `:1236-1239` (DuesInstallment→BillingGroup)
- **Senaryo:** Uygulama seviyesindeki koruma kontrolleri (UnitsController, DuesTypesController, BillingGroupService) check-then-act — race'e açık ve gelecekteki herhangi bir delete yolu bypass eder. Tek satır silme, Collections + CollectionAllocations'ı (para geçmişi) sessizce toplu siler.
- **Fix:** `Collection.Unit`, `Collection.BillingGroup`, `DuesInstallment.BillingGroup`, `BillingGroup.DuesType` için açıkça `DeleteBehavior.Restrict` + migration.

### H8. HTTPS redirect/HSTS ölü kod; auth cookie hiçbir zaman `Secure` değil
- **Yer:** `Program.cs:53-54` (`UseUrls("http://0.0.0.0:{port}")`), `:65, :86`; Dockerfile/nixpacks.toml plain HTTP 8080
- **Sorun:** Kestrel sadece HTTP dinliyor → `UseHttpsRedirection()` no-op, HSTS header'ı hiç gönderilmiyor. `UseForwardedHeaders` yok; TLS sonlandıran proxy arkasında `Request.IsHttps` false kalır ve Identity cookie'si (`SecurePolicy=SameAsRequest`) **Secure flag'siz** verilir.
- **Fix:** Auth'dan önce `ForwardedHeadersMiddleware` (XForwardedProto) ekleyin ve `ConfigureApplicationCookie(o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always)`.

---

## MEDIUM

### M1. Open redirect — doğrulanmamış `returnUrl`
- **Yer:** `Controllers/CollectionsController.cs:88,94,123`; `Controllers/ReportsController.cs:295,323,329,335`
- **Senaryo:** `returnUrl=https://evil.example/login` içeren link → işlem sonrası saldırgan sitesine 302 (phishing klonu). Aynı dosyada doğru örnek zaten var (`RedirectAfterSave`, CollectionsController.cs:403-411, `Url.IsLocalUrl` kullanıyor).
- **Fix:** Tüm bu noktaları `Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(...)` üzerinden geçirin.

### M2. Transfer çifti heuristik eşleştiriliyor — edit/delete yanlış bacağı bulabilir veya tek bacak bırakabilir
- **Yer:** `Controllers/CashBankController.cs:898-910` (`UpdateTransferTransaction`), `:1105-1116` (delete), `:1045-1056` (`FindTransferPair`: Amount+Date+Description)
- **Senaryo:** Aynı tutar/açıklama/timestamp'li iki transfer (legacy satırlar veya aynı import batch'i): birini düzenlemek *diğerinin* karşı bacağını değiştirir; silme yanlış karşı satırı kaldırır. Çift bulunamazsa delete tek bacağı siler — defter kalıcı dengesiz kalır.
- **Fix:** Her iki bacağa oluşturma anında `TransferGroupId` (Guid) yazın; eşleştirmeyi onunla yapın. Çifti bulunamayan transfer bacağının silinmesini reddedin.

### M3. `UpdateLedgerTransaction`/`UpdateCollectionTransaction` işlemi post edilen hesaba scope'lamıyor; transfer bacağını bozabiliyor
- **Yer:** `Controllers/CashBankController.cs:785` (`FindAsync(transactionId)` — hesap filtresi yok; delete'teki `:1094-1097` ile karşılaştırın), `:684-687` (collections aynı), `:798-806` (`IsTransfer` kontrolü yok)
- **Senaryo:** Bayat/tahrif edilmiş form X hesabının `transactionId`'sini Y hesabıyla post eder → satır sessizce Y'ye taşınır, iki bakiye de değişir. `transactionId` bir transfer bacağıysa tutarı/hesabı değişirken çifti dokunulmadan kalır → hesaplar arası para yaratılır/yok edilir.
- **Fix:** Delete'teki sahiplik predicate'iyle fetch edin; `entity.IsTransfer` ise reddedin ("transfer düzenleme formunu kullanın").

### M4. Çok adımlı import/yazma akışları transaction'sız — hata anında kısmi veri
- **Yer:** `Controllers/CollectionsController.cs:245-295` (satır başına commit; N. satırda hata → 1..N-1 kalıcı, düzeltilmiş dosya tekrar yüklenince mükerrer), `Controllers/UnitsController.cs:629-708` (4+ ayrı SaveChanges), `Controllers/DuesController.cs:168-169` (taksit kaydı + allocator ayrı commit'ler)
- **Fix:** Her akışı `await using var tx = await db.Database.BeginTransactionAsync(); ...; await tx.CommitAsync();` ile sarın — servisler ambient transaction'ı zaten algılıyor (`CollectionService.cs:52-54`), `CashBankController.CommitImport` doğru örnek.

### M5. `DuesInstallment.RemainingAmount` üzerinde lost-update race'i
- **Yer:** `Services/CollectionService.cs:195-219`, `Services/CollectionAdvanceAllocator.cs:34-57`
- **Senaryo:** İki kullanıcı aynı taksite eşzamanlı tahsilat girer (veya biri tahsilat girerken aidat üretimi allocator'ı çalıştırır): ikisi de Remaining=1000 okur, ikisi de 1000 allocate eder → Remaining=0 ama `SUM(Allocations) = 2000 > Amount`; borç raporu daireyi fazla alacaklandırır.
- **Fix:** `DuesInstallment`'a concurrency token (`[Timestamp]`/Postgres `xmin`) + `DbUpdateConcurrencyException`'da retry, veya Serializable transaction / `SELECT ... FOR UPDATE`.

### M6. Merged grup taksitlerinde (UnitId=null) unique index koruması yok + aidat üretimi check-then-insert
- **Yer:** `Data/ApplicationDbContext.cs:83-85` (`(BillingGroupId, Period, UnitId)` unique — PostgreSQL NULL'ları farklı sayar), `Services/DuesGenerationService.cs:111-133, 148-170`
- **Senaryo:** İki admin aynı dönemi eşzamanlı üretir → merged grup için mükerrer taksit (çift borç). Tek koruma check-then-insert.
- **Fix:** `(BillingGroupId, Period)` üzerinde `WHERE "UnitId" IS NULL` partial unique index, veya PG15+ `NULLS NOT DISTINCT`.

### M7. Açılış bakiyesi satırı tarihinden bağımsız olarak yürüyen bakiyede ilk sıraya zorlanıyor
- **Yer:** `Services/CashBankDetailService.cs:110-137` (sıralamadan sonra `Insert(0, ...)`)
- **Senaryo:** Hesapta 2025-05 tarihli işlemler varken kullanıcı 2026-01 tarihli açılış bakiyesi girer ("açılış bakiyesi tarihi düzenleme" özelliği bunu kolaylaştırdı): 2025 satırlarının `RunningBalance`'ı 2026 açılış tutarını içerir; açılış satırı listenin ortasında, üst/alt satırdan türetilemeyen bir bakiyeyle görünür.
- **Fix:** Açılış satırını sıralamadan *önce* `allRows`'a ekleyin ve tarih eşitliğinde önce gelecek şekilde tie-break verin: `OrderBy(r => r.Date).ThenBy(r => r.Source == "opening" ? 0 : 1)`.

### M8. `CollectionAdvanceAllocator` grup avanslarını `CollectionService`'ten farklı dağıtıyor
- **Yer:** `Services/CollectionAdvanceAllocator.cs:26-28` (`x.UnitId == collection.UnitId || x.UnitId == null` filtresi)
- **Senaryo:** Grup düzeyi tahsilatta `collection.UnitId` sadece temsilci daire; oluşturma anında fazla para tüm grubun taksitlerine yayılırken, sonradan allocator çalıştığında sadece temsilcinin taksitlerine gider. Paranın hangi daireye gideceği, borcun ödemeye göre *ne zaman* oluştuğuna bağlı hale gelir.
- **Fix:** `CollectionService` kuralını aynalayın; tahsilatın hedef dairesini `Collection` üzerinde persist etmek en temiz çözüm (H5 fix'iyle birleşir).

### M9. Borç raporu: merged grup avansı/açılış bakiyesi temsilci dairenin satırına yazılıyor; BlockId filtresi merged taksitleri düşürüyor
- **Yer:** `Services/ReportingService.cs:61` (filtre), `:100-109` (açılış bakiyeleri), `:126-132` (alacaklar)
- **Senaryo:** Merged grup (A+B) 1000 borçlu; 1000 TL tahsilat henüz allocate edilmemiş → rapor "grup satırı: Borç 1000" **ve** "A satırı: Alacak 1000" gösterir — net doğru ama iki satır da yanlış (export'larda hayalet borç + hayalet alacak). Ayrıca `BlockId` seçiliyken `(x.UnitId != null && ...)` tüm merged grup borcunu rapordan sessizce dışlar.
- **Fix:** Allocate edilmemiş alacağı taksitlerin kullanacağı anahtara yönlendirin (merged grupta `G:` satırı); BlockId için `x.UnitId == null && x.BillingGroup.Units.Any(u => u.Unit.BlockId == query.BlockId)` ekleyin.

### M10. `UnitStatementService` merged grup borcunu her üye dairenin ekstresinde tam tutarla tekrarlıyor
- **Yer:** `Services/UnitStatementService.cs:36-52` (taksitler), `:58-77` (allocation'lar)
- **Senaryo:** 2 daireli merged grupta 1000 TL taksit: her dairenin ekstresi (ve `UnitsController` bakiyesi) 1000 borç gösterir → site genelinde borç 2000 görünür.
- **Fix:** Merged borçları sadece temsilci/birleşik dairede gösterin veya "ortak" olarak işaretleyin; `Balance`'a tam tutarı her üyede saydırmayın.

### M11. Açılış bakiyesi tarih semantiği borç raporu ile ekstre arasında tutarsız
- **Yer:** `Services/ReportingService.cs:100-109` (`OpeningBalance`'ı koşulsuz düşer) vs `Services/UnitStatementService.cs:23` (`OpeningBalanceDate.HasValue` şartı arar)
- **Senaryo:** `OpeningBalance = 500`, `OpeningBalanceDate = null` olan daire: borç raporu 500 az borç gösterir, ekstre tam borcu gösterir — iki resmi rapor tam açılış bakiyesi kadar çelişir.
- **Fix:** Aynı koşulu borç raporuna da uygulayın (veya kuralı her yerden kaldırın).

### M12. CSV ayracı her satırda ayrı tespit ediliyor, tırnak içi karakterler sayılıyor
- **Yer:** `Services/CsvImportHelper.cs:38-41, 76-81`
- **Senaryo:** Noktalı virgüllü CSV'de `"Yılmaz, Ali, Veli, Ahmet"` gibi virgül yoğun bir satır ayraç tespitini virgüle çevirir → o satırın kolonları kayar; tutar/tarih yanlış alana düşer.
- **Fix:** Ayracı **bir kez header satırından** tespit edip `ParseLine`'a geçirin; sayarken tırnak içini atlayın.

### M13. Import'ta mükerrer tespiti ve idempotency yok
- **Yer:** `Services/CsvImportHelper.cs` + `Controllers/CashBankController.cs` `PreviewImport`/`CommitImport`
- **Senaryo:** Aynı ekstre iki kez import edilir (aylık export'lar çakışır veya commit formu çift post edilir) → tüm tahsilat/giderler ikiye katlanır, uyarı yok.
- **Fix:** Önizlemede (hesap, tarih, tutar, ReferenceNo) eşleşen satırları "muhtemel mükerrer" olarak `Include=false` işaretleyin; `CommitImport`'a tek kullanımlık token ekleyin.

### M14. Aidat üretiminde `payerType` parametresi yok sayılıyor — UI seçimi no-op
- **Yer:** `Services/DuesGenerationService.cs:67` (parametre hiç kullanılmıyor; `:108, :147`'de daire bazlı `unit.DuesPayerType` kullanılıyor), çağıran: `Controllers/DuesGenerationController.cs:27-29`
- **Senaryo:** Admin "Malik" seçer; `DuesPayerType.Tenant` ayarlı daireler yine kiracıyı sorumlu alır — sorumluluk ve hesap borç raporu admin'in seçtiğiyle uyuşmaz.
- **Fix:** Parametreyi kullanın ya da parametreyi ve form alanını kaldırın.

### M15. Bakiye raporu hesapsız tahsilatları "banka" sayıyor — raporlar CashBank ekranıyla çelişiyor
- **Yer:** `Controllers/ReportsController.cs:433` (`CashBoxId.HasValue ? "cash" : "bank"` — iki alan da null olan satırlar bankaya gider), `:98-110`
- **Senaryo:** Collections CSV import'u hesap set etmez; 10×500 TL import sonrası `/Reports/Balance` banka kapanışı `/CashBank` kartlarının toplamından 5.000 TL fazla — mutabakat imkânsız.
- **Fix:** Hesapsız satırları kasa/banka toplamlarından çıkarıp "Hesapsız tahsilat" olarak ayrı raporlayın, veya Collections import'una `AccountKey` zorunluluğu ekleyin.

### M16. Dashboard veri yokken uydurma rakamlar gösteriyor
- **Yer:** `Controllers/HomeController.cs:161-172` (sahte gider "tahmini": Maaşlar 186.000 vs.), `:217-226` (örnek nakit akışı); `Views/Home/Index.cshtml:42` ("5,4% geçen aya göre"), `:127` ("Tahmin güveni %82"); `Views/Shared/_Layout.cshtml:183` (sabit bildirim rozeti "7")
- **Senaryo:** Taze deployment'ta yönetim, ~412.900 TL "tahmini gider" ve 6 aylık uydurma nakit akışı görür — gerçek veriden ayırt edilemez.
- **Fix:** Boş liste dönüp view'da "yetersiz veri" durumu gösterin; en azından `IsSampleData` flag'i + filigran.

### M17. `DuesTypesController.Edit` / `IncomeExpenseCategoriesController.Edit`: kör `Update(model)` (overposting + 500)
- **Yer:** `Controllers/DuesTypesController.cs:46-55`, `Controllers/IncomeExpenseCategoriesController.cs:57-68`
- **Senaryo:** (a) Var olmayan `Id` post edilirse 404 yerine `DbUpdateConcurrencyException` → 500. (b) Domain entity tamamen client kontrolünde: kategorinin `Type`'ını Gider→Gelir çevirmek, o kategoriyi kullanan tüm tarihi yevmiye satırlarının işaretini geriye dönük değiştirir.
- **Fix:** Entity'yi id ile yükleyin, yoksa 404; sadece düzenlenebilir alanları kopyalayın; işlem referansı olan kategoride `Type` değişikliğini yasaklayın.

### M18. Tüm tabloyu belleğe çeken bakiye hesapları + N+1'ler (performans)
- **Yer:** `Controllers/CashBankController.cs:26-43` ve `Controllers/HomeController.cs:41-55` (her sayfa görünümünde tüm `Collection` + `LedgerTransaction`), `Controllers/ReportsController.cs:382-465` (Balance başına iki kez), `Services/CashBankDetailService.cs:47-50` (sayfadaki ≤25 transfer için tüm ledger tablosu), `Services/CollectionAdvanceAllocator.cs:11-32` (tahsilat başına sorgu), `Services/DuesGenerationService.cs:143-157` (daire başına 2 sorgu), derin `Include` zincirleri `AsSplitQuery()`'siz (`CollectionService.cs:9-30`, `ReportingService.cs:38-65`)
- **Senaryo:** Birkaç yıl veri sonrası (on binlerce satır) dashboard, CashBank ve raporlar saniyeler mertebesine düşer.
- **Fix:** SQL'de aggregate edin (`GroupBy` + `Sum`), transfer çiftini sayfadaki satırlar için tek sorguyla arayın, batch-load + `AsSplitQuery()`.

### M19. SQLite dalı `EnsureCreated()` kullanıyor — migration'lar hiç uygulanamaz
- **Yer:** `Program.cs:71-78`
- **Senaryo:** SQLite deployment `__EFMigrationsHistory`'siz şema anlık görüntüsü alır; sonraki migration'lar asla uygulanamaz. Son migration'ın PostgreSQL-quoted raw SQL'i (`20260701165925...cs:39-70`) SQLite'ta zaten patlar. Commit'li `app.db` bu sapmayı şimdiden gösteriyor (sadece Identity tabloları var).
- **Fix:** SQLite dalını kaldırın veya provider koşullu SQL ile `Migrate()` kullanın.

### M20. "Tek seferlik" blok adı düzeltmesi her başlangıçta çalışıyor ve boot'u çökertebilir
- **Yer:** `Program.cs:83, 110-124` (her start'ta blok adlarından `" Blok"` ekini kırpıyor)
- **Senaryo:** "A" ve "A Blok" birlikte varsa `SaveChangesAsync` unique index'i (`ApplicationDbContext.cs:33-35`) ihlal eder → startup scope'unda yakalanmayan exception, uygulama açılamaz. Ayrıca meşru adlandırılmış gelecekteki blokları da sessizce yeniden adlandırır.
- **Fix:** Fixup'ı silin (işini bitirdi) veya korumalı manuel migration'a çevirin.

### M21. Zayıf şifre politikası + e-posta onayı yok
- **Yer:** `Program.cs:34-38` (uzunluk 6, büyük harf/sembol yok, `RequireConfirmedAccount=false`)
- **Senaryo:** C1 ile birleşince finans uygulamasında trivially brute-force'lanabilir hesaplar.
- **Fix:** Uzunluk ≥ 10, büyük harf + rakam şartı; lockout ayarlarını gözden geçirin.

### M22. Tüm stack release-candidate paketlere sabitlenmiş
- **Yer:** `Kumburgaz.Web.csproj:13-22` (`10.0.0-rc.1.25451.107` ASP.NET/EF, `Npgsql...10.0.0-rc.1`)
- **Senaryo:** .NET 10 Kasım 2025'te GA oldu; RC'ler desteklenmiyor ve sonraki güvenlik yamalarını almıyor (bugün Temmuz 2026).
- **Fix:** Tüm `10.0.0-rc.1*` referanslarını güncel stable `10.0.x`'e yükseltin.

### M23. Drive sync-conflict "(1)" dosyaları bir migration'ın *tek* kopyası
- **Yer:** `Data/Migrations/20260511171111_AddAccountsAndDuesResponsibleAccount(1).cs`, `...Designer(1).cs`, `Areas/Identity/Pages/_ViewImports(1).cshtml` — hepsi git'te tracked; orijinal adlı dosyalar yok
- **Sorun:** Şu an derlenir ve migration geçmişi sağlam (sınıf adı ve `[Migration]` attribute'u doğru — dosya adı EF/C# için önemsiz). Ama Drive orijinal adları geri getirirse çift sınıf tanımı → CS0101 build kırılması. `_ViewImports(1).cshtml` özel `_ViewImports` adına sahip olmadığı için başıboş Razor view olarak derleniyor (byte-identical, zararsız).
- **Fix:** İki migration dosyasını `git mv` ile kanonik adlarına taşıyın; `_ViewImports(1).cshtml`'i silin.

### M24. Blok adı inline `onsubmit` confirm'ine gömülüyor — stored JS injection (admin self-XSS)
- **Yer:** `Views/Blocks/Index.cshtml:57-58`
- **Senaryo:** Razor `'` karakterini `&#39;` yapar ama HTML parser JS motoru görmeden geri çözer; `( ) ; /` encode edilmez. `');alert(document.cookie);//` adlı blok, Blocks listesini açan her admin'de JS çalıştırır. Uygulamadaki server verisi gömen tek `confirm()` bu.
- **Fix:** JS string'ini server verisinden kurmayın: statik `confirm('Bu blok silinsin mi?')` + gerekiyorsa `data-block-name` attribute'u.

### M25. CSV export'ta formül enjeksiyonu
- **Yer:** `Services/CsvExportHelper.cs:23-37` (`=`, `+`, `-`, `@` ile başlayan değerler aynen yazılıyor)
- **Senaryo:** Tahsilat notuna `=HYPERLINK("http://evil/x";"toplam")` giren biri, export'u Excel'de açan muhasebecide formül çalıştırır.
- **Fix:** `Escape` içinde `=+-@` ile başlayan değerlerin önüne `'` ekleyin. (ClosedXML Excel export'u etkilenmiyor.)

### M26. İki para kolonunda decimal precision eksik
- **Yer:** `Models/DomainModels.cs:98` (`Unit.OpeningBalance`), `:166` (`DuesType.Amount`) — snapshot'ta sınırsız `numeric`, diğer tüm para kolonları `numeric(18,2)`
- **Fix:** İkisine de `HasPrecision(18,2)` + migration.

### M27. `app.db` SQLite dosyası git'te tracked
- **Yer:** repo kökü (`.gitignore` girdisi tracked dosyaya etkisiz)
- **Senaryo:** Şu an boş Identity şeması (0 kullanıcı, doğrulandı) ama lokal login testi şifre hash'lerini commit'ler.
- **Fix:** `git rm --cached app.db`.

---

## LOW

### L1. `LedgerController.Delete` transfer'in tek bacağını silebiliyor
- **Yer:** `Controllers/LedgerController.cs:186-199` — `IsTransfer` kontrolü ve çift işleme yok (CashBank'taki delete'in aksine). **Fix:** `IsTransfer` satırlarını reddedin veya çift silme mantığını yeniden kullanın.

### L2. `LedgerController.Create/Edit` kategori id'sini doğrulamıyor
- **Yer:** `Controllers/LedgerController.cs:110-130, 158-182` — varlık/tip kontrolü yok (`CashBankController.CreateLedger:738-745` doğru yapıyor). Tahrif edilmiş form gideri Gelir kategorisine yazar; olmayan id → FK 500. **Fix:** CashBank'taki `AnyAsync(id + beklenen Type)` kontrolünü ekleyin.

### L3. `CashBankController.ExportCsv` escape'siz CSV üretiyor
- **Yer:** `Controllers/CashBankController.cs:963-974` — `;`, tırnak veya `=` içeren açıklama kolonları kaydırır / formül enjeksiyonu. **Fix:** Diğer tüm export'ların kullandığı `CsvExportHelper.BuildCsv`'yi burada da kullanın.

### L4. `DuesGenerationController.Index`: doğrulanmamış `period` → 500
- **Yer:** `Controllers/DuesGenerationController.cs:17` — `?period=x` ile `int.Parse(period[..4])` fırlatır. **Fix:** Parse'tan önce `PeriodHelper.IsValid` kontrolü, geçersizse güncel döneme düşün.

### L5. Basit CRUD controller'larda doğrudan domain-model binding + doğrulanmamış `DocumentRecord.Url`
- **Yer:** `AnnouncementsController.cs:24,42`, `DocumentsController.cs:25,43`, `RequestsController.cs:33`, `DuesTypesController.cs:21`, `BlocksController.cs:34`, `IncomeExpenseCategoriesController.cs:27` — overposting yüzeyi; `javascript:` URL'i diğer adminlere stored-XSS-vari link olur. **Fix:** ViewModel/`[Bind]` allow-list; `Url` şemasını http/https ile sınırlayın.

### L6. UTC/lokal tarih karışıklığı
- **Yer:** `Controllers/DuesController.cs:64` (UTC `DueDate` vs lokal `DateTime.Today` — gecikme durumu UTC+3'te gece yarısı civarı yanlış saatte döner), `Controllers/HomeController.cs:14` (`UtcNow.Date` — 00:00-03:00 TR arası dünün tarihi), `Models/DomainModels.cs:314,326` (`DateTime.Today` default'ları Kind=Local — gelecekte `EnsureUtc`'siz bir kayıt yolu Npgsql'de fırlatır). **Fix:** Tarih bazlı iş mantığı için tek saat standardı; default'ları `DateTime.UtcNow.Date` yapın.

### L7. `PeriodHelper.IsValid` bozuk dönemleri kabul ediyor
- **Yer:** `Services/PeriodHelper.cs:12-18` — `" 999-1000"` (boşluk), `"-001-0000"` geçer (`int.TryParse` toleransı). **Fix:** İki 4 karakterlik dilimde `All(char.IsAsciiDigit)`.

### L8. Gün başlığındaki "Net" sadece sayfadaki satırlardan hesaplanıyor
- **Yer:** `Services/CashBankDetailService.cs:212-221` — sayfalama bir günü bölünce kısmi toplam. **Fix:** Gün netlerini `Skip/Take` öncesi `filteredList`'ten hesaplayın.

### L9. `LastTransactionAt` açılış pseudo-satırını içeriyor
- **Yer:** `Services/CashBankDetailService.cs:138`. **Fix:** `Kind == TxKind.Acilis` hariç tutun.

### L10. Kategori tipi literal `"Gelir"` ile karşılaştırılıyor
- **Yer:** `Services/CashBankDetailService.cs:92` — `"Income"`/`"gelir"` olarak seed/import edilmiş satır gider gibi işaretlenir. **Fix:** `CategoryTypeHelper.Normalize` ile karşılaştırın.

### L11. `CsvImportHelper` tırnak içi satır sonlarını işleyemiyor
- **Yer:** `Services/CsvImportHelper.cs:19-33` — Excel'in çok satırlı notlu CSV'leri iki bozuk satıra bölünür. **Fix:** `inQuotes` durumunu satırlar arası taşıyan okuma veya dengesiz tırnaklı satırı reddetme.

### L12. `FlexibleDecimalParser` Unicode eksi işaretini (U+2212) reddediyor
- **Yer:** `Services/FlexibleDecimalParser.cs:15-20`. **Fix:** Normalizasyonda `.Replace('−', '-')`.

### L13. Üyesiz gruba `UnitId = 0` atanıyor
- **Yer:** `Services/DuesGenerationService.cs:188-192` — boş listede `FirstOrDefault()` = 0 → FK ihlali, dönem üretimi ham exception'la iptal. **Fix:** `unitIds.Count == 0 ? null : unitIds[0]`.

### L14. DuesType'sız gruplar 0 tutarlı kalıcı `Open` taksit üretiyor
- **Yer:** `Services/DuesGenerationService.cs:91` (`group.DuesType?.Amount ?? 0m`) — hiçbir zaman `Paid` olamayan çöp satırlar. **Fix:** DuesType'sız/0 tutarlı grupları atlayın veya üretimi validation hatasıyla durdurun.

### L15. `_SidePanel` "Notlar" sekmesi çalışmıyor (ölü UI)
- **Yer:** `Views/CashBank/_DetailParts/_SidePanel.cshtml:351-356` — textarea + "Kaydet" butonu form dışında ve handler'sız; yazılan not sessizce kaybolur. **Fix:** POST aksiyonuna bağlayın veya butonu kaldırın.

### L16. Default DB kimlik bilgileri commit'li; `AllowedHosts: "*"`
- **Yer:** `appsettings.json:3,11` (`postgres/postgres`) — lokal default'lar; prod'un env override kullandığını doğrulayın, host filtreleme düşünün.

### L17. Aynı dairede iki aktif Malik/Kiracı satırı temsil edilebilir
- **Yer:** `ApplicationDbContext.cs:44-45` — `(UnitId, Role, Active)` index'i unique değil. **Fix:** Rol başına tek aktif invariant'sa `WHERE "Active"` filtreli unique index.

### L18. `PaymentChannel` ile `CashBoxId`/`BankAccountId` arasında DB kısıtı yok
- **Yer:** `Models/DomainModels.cs:243-247` — sadece `CashBoxId` set edilmiş `Bank` tahsilatı temsil edilebilir; hesap bazlı bakiyeler sessizce yanlış raporlar. **Fix:** Tablo check constraint'i.

---

## Doğrulanıp sorunsuz bulunanlar

- **Anti-forgery:** 18 controller'daki tüm state değiştiren POST aksiyonları `[ValidateAntiForgeryToken]` taşıyor; tüm formlar token içeriyor; hiçbir GET state değiştirmiyor.
- **XSS:** M24 dışında `@Html.Raw` ile kullanıcı verisi yok; `site.js` arama dropdown'ı `escapeHtml()` kullanıyor.
- **Kuruş/yuvarlama:** Allocation'lar `Math.Min` + exact `decimal` — parçaların toplamı her zaman bütüne eşit.
- **Delete koruması:** Blocks, DuesTypes, Units, Accounts, BillingGroups, kasa/banka hesapları ve dönem silme bağımlı kayıtları kontrol ediyor (ama bkz. H7 — DB seviyesinde değil).
- **`CollectionService.DeleteAsync`** allocation rollback'ini transaction içinde doğru yapıyor; ambient transaction algılama (`CurrentTransaction is null`) iç içe transaction hatasını önlüyor.
- **Son migration'ın data-migration SQL'i** kategori referanslarını silmeden önce null'luyor — FK orphan riski yok; Designer modeli snapshot'la birebir tutarlı.
- **`appsettings.Development.json`** git'te değil; detaylı hatalar Development'a doğru kısıtlanmış.

---

## Fix Öncelik Listesi

Sıralama: (kullanıcı etkisi × oluşma olasılığı) / fix maliyeti.

1. **C1 + C2 + M21 — Kimlik doğrulama paketi:** Register'ı kapatın, admin seed şifresini config'e taşıyın, şifre politikasını sıkılaştırın. *Uygulama internete açıksa bugün yapılmalı.*
2. **H1 — HomeController `[Authorize]`** (+ global fallback policy). Tek satırlık fix, anonim finansal veri sızıntısını kapatır.
3. **C3 — `FlexibleDecimalParser` "2.500" fix'i.** Sıradan Türkçe girdiyle sessiz 1000× hata; bir sonraki import/veri girişinden önce.
4. **C4 — Tahsilat düzenleme allocation kaybı.** Sıradan bir düzenlemede (not değişikliği bile) veri bozuyor.
5. **H2 + H3 + H4 — Tüm tutar parse yollarını `FlexibleDecimalParser`'da birleştirin** (CSV import'lar, `asp-for` formlar, `Replace(',', '.')` yerleri). C3 fix'i sonrası tek oturumluk, ×100 hatalarının tamamını kapatır.
6. **H5 + M8 — Tahsilatın hedef dairesini persist edin** ve hem `CollectionService` hem `CollectionAdvanceAllocator`'da kullanın. Yanlış daireye ödeme + tutarsız avans dağıtımını birlikte çözer.
7. **H6 — Transfer heuristiğini kaldırıp `IsTransfer` flag'ine güvenin.** Migration sonrası heuristik sadece zarar veriyor; küçük fix.
8. **H7 — Cascade delete'leri `Restrict`'e çevirin** (migration ile). Finansal geçmişin toplu silinme riskini DB seviyesinde kapatır.
9. **H8 — ForwardedHeaders + Secure cookie.** Prod reverse-proxy arkasındaysa oturum çalınabilirliğini kapatır.
10. **M2 + M3 — `TransferGroupId` ekleyin ve update aksiyonlarını hesaba scope'layın.** Transfer edit/delete'in defteri dengesiz bırakmasını önler.
11. **M4 + M5 + M6 — Transaction sarmalama + concurrency token + partial unique index.** Eşzamanlılık/yarım-yazma sınıfının tamamı.
12. **M12 + M13 + M25 + L3 + L11 — CSV katmanı:** header'dan tek ayraç tespiti, mükerrer import uyarısı, formül-enjeksiyon escape'i, CashBank export'unda `CsvExportHelper`.
13. **M9 + M10 + M11 + M15 — Rapor tutarlılık paketi:** merged grup satırları, BlockId filtresi, açılış bakiyesi semantiği, hesapsız tahsilat sınıflandırması. Raporlar birbiriyle mutabık hale gelir.
14. **M20 + M19 + M23 + M27 — Hijyen:** startup fixup'ını silin, SQLite/`EnsureCreated` dalını kaldırın, "(1)" dosyalarını yeniden adlandırın/silin, `app.db`'yi git'ten çıkarın, `.gitattributes` ekleyin.
15. **M22 — RC paketlerini stable 10.0.x'e yükseltin.**
16. **M14 + M16 + M17 + M24 + M26 — Orta öncelikli kalanlar:** payerType, sahte dashboard verisi, kör `Update`, Blocks confirm injection, decimal precision.
17. **LOW bulgular (L1-L18)** — fırsat buldukça; L4 (500 hatası), L13 (dönem üretimini kilitler) ve L6 (tarih kaymaları) öne alınabilir.
