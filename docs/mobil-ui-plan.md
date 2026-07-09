# Kumburgaz Mobil UI Plani (PWA)

Bu dosya mevcut ASP.NET Core MVC uygulamasinin uzerine kurulacak mobil oncelikli PWA katmaninin tasarimini ve asamali uygulama planini icerir.

Son guncelleme: 2026-07-09
Aktif branch: `codex/kumburgaz-improvement-plan`

## Yaklasim Ozeti

Karar: Native (SwiftUI/Flutter) uygulama YAPILMAYACAK. Bunun yerine ayni ASP.NET projesi icinde `Mobile` adinda bir MVC Area (`/m` route on eki) ve PWA kabugu (manifest + service worker + web push) eklenecek.

Gerekceler:

- Ayri bir SPA/API katmani gerekmez; mevcut cookie auth, `ModuleAuthorize` yetki matrisi ve ~40 domain servisi aynen yeniden kullanilir.
- App Store / Play Store yayini gerekmez; kullaniciya link vermek yeter. Telefonda "Ana Ekrana Ekle" ile uygulama gibi acilir (standalone).
- Push bildirim: Android Chrome'da dogrudan calisir; iOS 16.4+ icin kullanicinin uygulamayi ana ekrana eklemesi sarttir (kurulum yardim sayfasi ile anlatilacak).
- Sinirli kullanici sayisi icin en dusuk maliyetli ve en dusuk riskli yol budur.

Netlesen diger kararlar:

- Fis/fatura fotograflari PostgreSQL icinde saklanacak (bytea, ayri `Attachment` tablosu, sunucuda ~200-500KB'a sikistirma). Boylece `pg_dump` ile tek yedek her seyi kapsar. (SQLite dev ortaminda BLOB olarak ayni sekilde calisir.)
- Yeni `Sakin` rolu eklenecek: sadece erisimi olan daireleri gorur, mahsuplu gider girer, talep acar.
- Sakin girisleri ayri kullanici yonetimi ekraninda GORUNMEZ. Her malik/kiraci hesabi icin otomatik bir giris olusturulur; kullanici adi hesabin mevcut Id'sidir, sifre 5 haneli rasgele uretilir. Bu bilgiler "Kullanici Giris Bilgileri" raporunda listelenir.
- Bir hesabin erisebilecegi daireler yalnizca sahipligiyle sinirli degildir; sistem/site yoneticisi bir hesaba baska daireleri de tanimlayabilir (ornek: Alper Bahceliler B8 ve C24 sahibi ama C21'de yakini oturuyor ve onun aidatini o oduyor; C21 de erisimine eklenir).
- Ayri bir "Mahsup Kasasi" OLUSTURULMAZ. Mahsup islemleri sitenin normal ana kasasindan yapilir (tahsilat + gider ayni kasada, net etki sifir).

Gereksinim karsilama tablosu:

| # | Gereksinim | Plandaki karsiligi |
|---|---|---|
| 1 | Daire listesi + tahakkuk/odeme/borc detayi | `/m/Daireler` ve `/m/Daireler/Detay/{id}` ekranlari |
| 2 | Fotografli gider girisi, sunucuda saklama | `Attachment` tablosu (PostgreSQL bytea) + `/m/Gider/Yeni` |
| 3 | Sakin icin kendi dairesinden mahsuplu gider (normal kasada) | `Sakin` rolu + `MobileScopeService` + `MahsupService` |
| 4 | Kasa-Banka hareketleri | `/m/KasaBanka` |
| 5 | Basit raporlar (borclu/alacakli/kategori bazli gider) + giris bilgileri | `/m/Raporlar` + Kullanici Giris Bilgileri raporu |
| 6 | Duyuru + Talep + atama bildirimi (zil + push) | `Notification`/`PushSubscription` tablolari, web push |
| 7 | Sade dashboard | `/m` Panel — 3-4 kart, fazlasi yok |

## Mimari

### Mobile Area

- MVC Area `Mobile`, route: `m/{controller=Panel}/{action=Index}/{id?}` (`Program.cs`'te default route'un USTUNE eklenir).
- Controller'lar: `PanelController`, `DairelerController`, `GiderController`, `KasaBankaController`, `RaporlarController`, `DuyurularController`, `TaleplerController`, `BildirimlerController`, `HesapController` (profil/sifre) — hepsi `Areas/Mobile/Controllers/` altinda.
- Kendi layout'u: `Areas/Mobile/Views/Shared/_MobileLayout.cshtml` + `_BottomNav.cshtml` + `_TopBar.cshtml`. Masaustu view'lari hic degismez.
- Ornek route'lar: `/m`, `/m/Daireler`, `/m/Daireler/Detay/5`, `/m/Gider/Yeni`, `/m/Bildirimler`, `/m/Hesap/Sifre`.

### Yetkilendirme

- Mobil controller'lar mevcut `[ModuleAuthorize(...)]` attribute'unu aynen kullanir (GET=CanView, yazma=CanWrite mantigi degismez):
  - Panel -> `AppModules.Panel`, Daireler -> `Daireler`, Gider -> `Muhasebe`, KasaBanka -> `KasaBanka`, Raporlar -> `Raporlar`, Duyurular -> `Duyurular`, Talepler -> `Talepler`.
  - Bildirimler ve Hesap sadece `[Authorize]` (herkes kendi bildirim/profilini gorur).
- Yeni rol `Sakin` (`AppRoles.Sakin`), `SeedRolePermissionsAsync` tohumu: write = [Muhasebe, Talepler], view = [Panel, Daireler, Aidatlar, Duyurular].
- Guvenlik siniri: yeni `Services/SakinAreaRestrictionFilter.cs` (global `IAsyncAuthorizationFilter`) — `Sakin` rolundeki kullanici `Mobile` ve `Identity` area'lari disina cikamaz (GET -> `/m` redirect, yazma -> 403). Boylece masaustu controller'lara veri kapsami eklemek gerekmez; Sakin'in Muhasebe write yetkisi yalnizca mobilde islev gorur.

### Sakin veri kapsami: MobileScopeService

Yeni `Services/MobileScopeService.cs`:

- `kumburgaz_account_id` claim'ini okur (`ApplicationUserClaimsPrincipalFactory` bu claim'i zaten uretiyor ama bugune kadar hicbir yerde tuketilmiyordu).
- Izinli daireler = (hesabin aktif `UnitAccount` kayitlarindan gelen daireler) BIRLESIM (yeni `AccountUnitAccess` tablosundan tanimlanmis ek daireler).
- `GetAllowedUnitIdsAsync(user)`: Sakin icin bu birlesik daire listesi, yonetici/personel icin `null` (kisitsiz).
- `EnsureUnitAccessAsync(user, unitId)`: Sakin izinli olmayan daireye erisirse NotFound.
- Hicbir daireye erisimi olmayan Sakin giris yaptiginda hata degil, "Hesabiniz bir daireye baglanmamis, yonetici ile gorusun" uyarisi gosterilir.

### Sayfa modeli

Kural: tum ekranlar klasik Razor form + post-redirect-get. JSON yalnizca su uc yerde:

1. `GET /m/Bildirimler/Ozet` -> `{ okunmamis: 3 }` (zil rozeti, 60 sn'de bir fetch polling).
2. `POST /m/Bildirimler/Abone` ve `POST /m/Bildirimler/AbonelikSil` -> push aboneligi kaydi.
3. `GET /m/Daireler/BorcOzet/{unitId}` -> mahsup formunda secilen dairenin guncel acik borcu.

Fetch cagrilari same-origin oldugundan cookie otomatik gider; POST'larda anti-forgery token `RequestVerificationToken` header'i ile tasinir.

## Sakin Hesaplari, Giris Bilgileri ve Daire Erisimi

Bu bolum kullanicinin son isteklerinin ozudur.

### Hesap = giris kimligi

- Her malik/kiraci hesabi (`Account.AccountType` = Owner veya Tenant) mobil giris kimligidir. Personel/Tedarikci hesaplarina giris acilmaz.
- Kullanici adi: hesabin mevcut `Id`'si (ornek `147`). Yeni bir kullanici adi uretilmez.
- Teknik olarak her boyle hesap icin idempotent sekilde bir `ApplicationUser` olusturulur (`UserName = Account.Id`, `AccountId = Account.Id`, rol `Sakin`, gerekirse sentetik email `acc-{id}@kumburgaz.local`). Boylece mevcut cookie auth, claim factory ve `ModuleAuthorize` degismeden calisir. Bu kullanicilar ayarlar kullanici yonetimi ekraninda GIZLENIR (bkz. asagi).
- Bu isi yapan yeni servis: `Services/ResidentAccountService.cs`
  - `EnsureLoginAsync(account)` — hesap icin giris yoksa olusturur, 5 haneli sifre uretir, Identity sifresini set eder, plaintext'i saklar, `Sakin` rolunu ve `AccountId` baglantisini atar.
  - `ResetPasswordAsync(accountId, newPassword?)` — verilmezse yeni 5 haneli uretir; hem Identity hash'ini hem plaintext'i gunceller.
  - `ChangeOwnPasswordAsync(userId, newPin)` — sakin kendi sifresini degistirir (5 haneli PIN sart); plaintext de guncellenir.
  - `GetCredentialsAsync()` — rapor icin hesap + kullanici adi + sifre + daireler.

### Sifre saklama

- 5 haneli rasgele sayisal sifre (`10000-99999`). Sistem yoneticisi sifreyi GOREBILMELI oldugu icin geri okunabilir saklanir: `Account` uzerinde `MobilePassword` (string) alani.
- Bu bilincli bir dusuk-hassasiyet tercihidir: sifreler yalnizca bu uygulamaya ozel 5 haneli PIN'lerdir, disarida kullanilmaz. Sakin kendi sifresini degistirdiginde de 5 haneli PIN olarak kalir (serbest parola degil), boylece baska yerde tekrar kullanim riski dusuk tutulur.
- Istege bagli sertlestirme (opsiyonel, sart degil): ASP.NET Data Protection ile sifreyi sifreli saklayip gosterirken cozmek. Docker/Coolify'da anahtar kaliciligi gerektirdiginden ilk surumde plaintext kolon tercih edilir.

### Ayarlar kullanici yonetiminde gizleme

- Mevcut `SystemUsersController` / kullanici yonetimi ekrani yalnizca PERSONEL kullanicilarini listeler; `Sakin` rolundeki (hesaba bagli) kullanicilar bu listeden filtrelenir.
- Boylece yuzlerce sakin girisi ayarlar ekranini kalabalik yapmaz; sakin girisleri Hesaplar + rapor uzerinden yonetilir.

### Kullanici Giris Bilgileri raporu

- Raporlar altinda yeni sayfa: "Kullanici Giris Bilgileri" (`/Reports/LoginCredentials` masaustu, `/m/Raporlar/GirisBilgileri` mobil).
- Icerik: hesap adi, tur (malik/kiraci), telefon, erisimindeki daireler, kullanici adi (= hesap Id), sifre (plaintext).
- Yetki: yalnizca SistemYonetici (sifre gorunur oldugu icin). Bu rapor `ReportingService` yerine dogrudan `ResidentAccountService.GetCredentialsAsync` kullanir.
- Excel'e aktarma (mevcut `ClosedXML` altyapisi) eklenebilir; boylece yonetici sifreleri sakinlere dagitabilir.

### Coklu daire erisimi (yeni AccountUnitAccess)

- Yeni tablo `AccountUnitAccess` (Id, AccountId, UnitId, CreatedAt, CreatedByUserId/UserName). Sahiplikten (UnitAccount) BAGIMSIZ ek "gorme + odeme" yetkisidir.
- Hesap duzenleme ekrani (`AccountsController.Edit` + view) genisletilir:
  - Sahip/kiraci oldugu daireler (UnitAccount'tan) salt-okunur rozetlerle GOSTERILIR.
  - Sistem/site yoneticisi, hesaba ait olmasa bile herhangi bir daireyi ek erisim olarak EKLEYIP/CIKARABILIR (`AccountUnitAccess`). Ornek: Alper Bahceliler'in hesabina C21 eklenir.
  - Ayni ekranda SistemYonetici icin sifre bolumu: mevcut plaintext'i gorme + "Yeni sifre uret / degistir" butonu (`ResidentAccountService.ResetPasswordAsync`).
- Erisim duzenleme yetkisi Hesaplar modulu write (SiteYonetici de yapabilir); sifre gorme/degistirme yalnizca SistemYonetici.
- `MobileScopeService` bu tabloyu UnitAccount ile birlestirir; sakin ek tanimli dairelerin borc/tahakkuk/odemesini gorur ve o daire icin mahsuplu gider girebilir.

### Sifre self-servis

- Sakin kendi sifresini `/m/Hesap/Sifre` ekranindan degistirir (eski PIN + yeni 5 haneli PIN). `ChangeOwnPasswordAsync` hem Identity hem plaintext'i gunceller.

## Yeni Veri Modeli

Migration'lar asamalara bolunur (bkz. Asamali Uygulama). Entity'ler `Models/MobileModels.cs` (veya `DomainModels.cs`) icine, DbSet'ler `Data/ApplicationDbContext.cs`'e.

### AccountUnitAccess (coklu daire erisimi) — Asama 2

- Alanlar: `Id`, `AccountId`, `UnitId`, `CreatedAt`, `CreatedByUserId/UserName`.
- Index: `(AccountId, UnitId)` unique.

### Account degisikligi — Asama 2

- Yeni alan: `MobilePassword` (string, 5 haneli PIN plaintext; yalnizca Owner/Tenant hesaplarinda dolar).

### Attachment (fis/fatura fotografi) — Asama 3

- Alanlar: `Id`, `EntityType` ("LedgerTransaction"), `EntityId`, `FileName`, `ContentType`, `ByteSize`, `byte[] Content` (PostgreSQL bytea / SQLite BLOB), `CreatedAt`, `CreatedByUserId/UserName` + `ISoftDeletable` alanlari.
- Index: `(EntityType, EntityId)`.
- Liste sorgulari `Content` kolonunu SELECT etmez (projection); icerik yalnizca `GET /m/Gider/Ek/{id}` action'inda `FileStreamResult` olarak doner.
- Sikistirma: yeni `Services/ImageAttachmentService.cs`, paket `SixLabors.ImageSharp` — uzun kenar max 1600px, JPEG kalite ~75, hedef 200-500KB; girdi limiti 15MB, cikti 1MB'i asarsa kalite dusurulerek tekrar denenir.

### MahsupIslem (baglanti tablosu) — Asama 3

- Alanlar: `Id`, `CollectionId`, `LedgerTransactionId`, `UnitId`, `CreatedAt`, `CreatedByUserId/UserName` + `ISoftDeletable`.
- Mevcut `Collection` / `LedgerTransaction` tablolarina alan EKLENMEZ; iliski bu tablodan izlenir.

### Notification — Asama 4

- Alanlar: `Id`, `RecipientUserId`, `Type` (TalepAtama/TalepDurum/Duyuru/Sistem), `Title`, `Body`, `LinkUrl`, `CreatedAt`, `ReadAt` (null = okunmamis).
- Index: `(RecipientUserId, ReadAt)`.

### ServiceRequest degisikligi — Asama 4

- Yeni alan: `AssignedToUserId` (string, AspNetUsers'a soft referans). Mevcut `AssignedTo` string'i gorunen ad olarak kalir (eski kayitlar bozulmaz).

### PushSubscription — Asama 5

- Alanlar: `Id`, `UserId`, `Endpoint` (unique), `P256dh`, `Auth`, `UserAgent`, `CreatedAt`, `FailCount`.
- Push servisi 404/410 yanit alinca kaydi siler.

### Dev ortami notu

SQLite dev ortami `EnsureCreated` kullaniyor; migration'lar yalnizca PostgreSQL'de kosar. Sema degisikliginden sonra dev'de `app.db` silinip yeniden olusturulmali.

## Mahsuplu Gider Islemi

Senaryo: Sakin (veya yonetici) daire adina bir site gideri oder; tutar aidat borcundan dusulur, gider muhasebeye islenir, kasa net etkisi sifirdir.

Yeni `Services/MahsupService.cs`, `CreateAsync` tek `BeginTransactionAsync` icinde su kayitlari olusturur (`CollectionService.CreateAsync` dis transaction'i destekliyor — `CurrentTransaction is null` kontrolu mevcut):

1. Collection (aidat tahsilati): `PaymentChannel.Cash`, kasa = sitenin ANA KASASI (ayri bir mahsup kasasi yok), en eski acik taksite dagitim mevcut serviste hazir; sorumlu hesap `AccountAssignmentService.ResolveResponsibleAccountIdAsync` ile cozulur.
2. LedgerTransaction (gider): ayni tarih, secilen Gider kategorisi, ayni tutar, ayni ana kasa. Aciklama: "Mahsup - {daire} - {aciklama}".
3. Attachment kayitlari: Sakin icin en az 1 fotograf ZORUNLU, yonetici icin istege bagli.
4. MahsupIslem: iki kaydin Id'lerini baglar.
5. AuditLog: tum adimlar ortak `CorrelationId` ("mahsup-{guid}") ile yazilir.

Ana kasa secimi:

- Mahsup, sitenin ana kasasinda yapilir. Ana kasa = ilk aktif `CashBox` (Id sirasina gore). Birden fazla kasa varsa ileride Ayarlar'da "varsayilan kasa" secimi eklenebilir.
- Tahsilat (+) ve gider (-) ayni kasada oldugundan kasa bakiyesi etkilenmez; hareketler ana kasanin normal hareket listesinde seffaf sekilde gorunur.

Kurallar:

- Dogrulama: tutar > 0, kategori aktif ve Gider tipinde, daire erisimi `MobileScopeService.EnsureUnitAccessAsync` ile kontrol.
- Borcu asan tutara IZIN verilir; fazlasi avans/alacak olur (mevcut fazla-tahsilat mantigiyla tutarli). UI acik borcu gosterir, asim durumunda "Fazlasi avans olarak kaydedilecek" uyarisi verir.
- Silme: `MahsupService.DeleteAsync` butun olarak siler (tahsilat allocation'lari geri acilir + gider/ek/mahsup soft delete + audit). Sakin kendi kaydini silemez (yanlislik varsa talep acar). Masaustu `CollectionsController.Delete` ve `LedgerController.Delete` aksiyonlarina koruma eklenir: mahsuba bagli kayit tekil silinemez, "mahsubu butun olarak silin" mesaji gosterilir.

## Bildirimler ve Web Push

- `Services/NotificationService.cs`: `NotifyAsync(userId, type, title, body, linkUrl)` -> (1) Notification satiri INSERT, (2) best-effort push. Push hatasi bildirimi engellemez.
- Push paketi: `Lib.Net.Http.WebPush` (VAPID destekli). VAPID anahtarlari env ile: `Push__Subject`, `Push__PublicKey`, `Push__PrivateKey` (Coolify'da env var; private key appsettings.json'a KONMAZ). Anahtar tanimli degilse push katmani sessizce devre disi kalir, zil calismaya devam eder.
- Gonderim: `Channel<T>` tuketen `PushDispatchHostedService` arka planda calisir (mevcut `BackupHostedService` kalibi ornek alinir); request'i bekletmez. 404/410 donen abonelik otomatik silinir.
- Tetikleyiciler:
  1. Talep atamasi (asil gereksinim): `AssignedToUserId` degistiginde atanana "Size yeni talep atandi: {Title}", link `/m/Talepler/Detay/{id}`.
  2. Talep durum degisimi -> talebi acana (opsiyonel, Asama 4-5).
  3. Duyuru yayini -> tum aktif kullanicilara fan-out (kucuk site icin sorun degil).
- `wwwroot/sw.js` icinde `push` ve `notificationclick` handler'lari (bildirime tiklaninca ilgili `/m/...` sayfasi acilir).
- `/m/Bildirimler` sayfasinda "Anlik bildirimleri ac" butonu: `Notification.requestPermission()` + `pushManager.subscribe()` + `POST /m/Bildirimler/Abone`.
- Zarif dusus: zil ikonu + Notification tablosu HER ZAMAN calisir (polling); push yalnizca destekleyen ve izin veren cihazlarda.

## PWA Kabugu

Yeni dosyalar:

- `wwwroot/manifest.webmanifest`: name "Kumburgaz Site Yonetimi", short_name "Kumburgaz", start_url "/m", scope "/", display "standalone", tema renkleri, ikonlar.
- `wwwroot/img/icons/`: icon-192.png, icon-512.png, icon-512-maskable.png, apple-touch-icon.png (180px).
- `wwwroot/sw.js` (kok kapsam), `wwwroot/js/mobile.js` (sw kaydi, zil polling, push abonelik, fotograf onizleme), `wwwroot/css/mobile.css` (alt sekme cubugu + kart stilleri; Bootstrap 5 uzerine ince katman), `wwwroot/offline.html`.
- CDN kullanilmaz; her sey vendored (mevcut yaklasim korunur).

Cache stratejisi (`sw.js`):

- Statikler (`/lib/`, `/css/`, `/js/`, `/img/`): cache-first, versiyonlu cache adi (`kumburgaz-static-v1`); aktivasyonda eski cache'ler silinir.
- HTML sayfalar (GET): network-first, basarisizlikta `offline.html`. Yanitlar cache'e YAZILMAZ (auth'lu icerik cihazda saklanmaz — guvenlik kurali).
- POST istekleri ve `/Identity/*`: service worker hic dokunmaz (bypass).

iOS notlari: `_MobileLayout`'a apple-touch-icon + meta etiketleri eklenir; `/m/Yardim/Kurulum` sayfasi Safari "Paylas -> Ana Ekrana Ekle" adimlarini ve push icin iOS 16.4+ sartini anlatir.

## Ekran Ekran Mobil UI

Genel iskelet: ustte sayfa basligi + sag ustte zil ikonu (okunmamis rozeti), altta 5 sekmeli sabit nav: Panel | Daireler | Gider | Raporlar | Diger. Sakin'de Raporlar sekmesi gizlenir (4 sekme). Ikonlar inline SVG (CDN yok).

| Ekran | Route | Icerik | Roller |
|---|---|---|---|
| Panel | `/m` | 3-4 sade kart: donem aidat tahsilati, borclu daire sayisi + toplam borc, bu ay giderler (ilk 3 kategori), gecen ay giderler. Sakin varyanti: erisimindeki dairelerin bakiye karti, son 3 odeme, son duyurular | Tumu (Sakin scoped) |
| Daire listesi | `/m/Daireler` | Blok bazli gruplu liste, arama; her satirda daire adi + bakiye rozeti (kirmizi borc / yesil alacak). Sakin: yalnizca erisimindeki daireler | Daireler view |
| Daire detay | `/m/Daireler/Detay/{id}` | Bakiye ozet karti; sekmeler: Tahakkuklar (donem, tutar, kalan) ve Odemeler (tarih, tutar, kanal). `UnitLedgerService` + `UnitStatementService` | Daireler view + scope |
| Gider listesi | `/m/Gider` | Son giderler (tarih, kategori, tutar, atas simgesi = ekli foto). Sakin: yalnizca kendi mahsup kayitlari | Muhasebe view |
| Gider ekle | `/m/Gider/Yeni` | Yonetici/Muhasebe: normal gider formu (kategori, tutar, kasa/banka, aciklama, opsiyonel foto) + "Mahsuplu gider" secenegi. Sakin: ZORUNLU mahsup akisi — daire (erisimindekiler), kategori, tutar (acik borc gosterilir), aciklama, zorunlu foto (`<input type="file" accept="image/*" multiple>` — kamera ZORUNLU degil, kullanici galeriden de secebilir) | Muhasebe write |
| Ek goruntule | `/m/Gider/Ek/{id}` | Fotografi dondurur (yetki: kaydin sahibi Sakin veya Muhasebe view) | scoped |
| Kasa-Banka | `/m/KasaBanka` | Kasa/banka secici + gunluk gruplu hareket listesi (`CashBankDetailService`) | KasaBanka view (Sakin goremez) |
| Raporlar | `/m/Raporlar` | Alt sayfalar: Borclular, Alacaklilar, kategori filtreli giderler; ayrica SistemYonetici icin Kullanici Giris Bilgileri. `ReportingService` yeniden kullanilir | Raporlar view |
| Duyurular | `/m/Duyurular`, `/Detay/{id}` | Liste + detay; yonetici icin "Yeni" butonu | Duyurular view/write |
| Talepler | `/m/Talepler`, `/Detay/{id}`, `/Yeni` | Liste (durum rozetleri), detay (durum degistir, kullanici secerek atama), yeni talep (baslik, aciklama, daire, oncelik). Sakin: erisimindeki dairelerle sinirli, atama yapamaz | Talepler view/write |
| Bildirimler | `/m/Bildirimler` | Okunmamis/okunmus liste; tiklaninca ReadAt set + LinkUrl'e git; "Tumunu okundu isaretle"; "Anlik bildirimleri ac" butonu | Tum girisliler |
| Sifre | `/m/Hesap/Sifre` | Sakin kendi 5 haneli PIN'ini degistirir | Tum girisliler |
| Diger | `/m/Diger` | Menu: Duyurular, Talepler, Bildirimler, Sifre Degistir, Kasa-Banka (rol varsa), Kurulum Yardimi, masaustu surum linki (Sakin haric), cikis | Tumu |

## Asamali Uygulama Plani

### Asama 1: Mobil iskelet + PWA kabugu

Durum: Baslamadi.

Kapsam:

- Mobile Area, route, `_MobileLayout`, alt nav, `mobile.css`.
- `manifest.webmanifest`, ikonlar, `sw.js` (yalnizca statik cache + offline sayfasi, push yok), sw kaydi.
- Panel (yonetici kartlari) + Daire listesi/detayi (tum daireler, mevcut servislerle).

Kabul kriterleri:

- Telefonda `/m` acilir, ana ekrana kurulur, standalone acilir.
- Panel kartlari masaustu dashboard ile ayni rakamlari gosterir.
- Daire detayinda tahakkuk/odeme/bakiye dogru.

### Asama 2: Sakin rolu, hesap girisleri ve daire erisimi

Durum: Baslamadi.

Kapsam:

- Migration `AddResidentAccounts`: `AccountUnitAccess` tablosu + `Account.MobilePassword` alani.
- `AppRoles.Sakin`, RolePermission tohumu, `MobileScopeService` (UnitAccount + AccountUnitAccess birlesimi), `SakinAreaRestrictionFilter`.
- `ResidentAccountService`: giris olusturma, 5 haneli sifre, plaintext saklama, self-servis/yonetici sifre degisimi.
- Startup `SeedResidentAccountsAsync` (idempotent): mevcut tum Owner/Tenant hesaplarina giris + sifre uretir; yeni hesap olustugunda da otomatik cagrilir (`AccountsController.Create`).
- `SystemUsersController` / kullanici yonetimi ekrani `Sakin` kullanicilarini gizler.
- `AccountsController.Edit` genisletilir: erisim dairelerini goster + yonetici ek daire ekleyip/cikarabilir; SistemYonetici sifre gorme/degistirme.
- Raporlar altinda Kullanici Giris Bilgileri raporu (SistemYonetici, Excel export).
- Sakin Panel/Daireler kapsami; `/m/Hesap/Sifre` self-servis.

Kabul kriterleri:

- Sakin, kullanici adi = hesap Id ve 5 haneli PIN ile giris yapar; masaustu URL'lerine giremez (redirect/403).
- Sakin yalnizca erisimindeki daireleri gorur; yoneticinin ek tanimladigi daire (ornek C21) de gorunur.
- Sakin girisleri ayarlar kullanici yonetiminde gorunmez; Kullanici Giris Bilgileri raporunda gorunur.
- SistemYonetici bir hesabin sifresini gorur ve degistirir; sakin kendi PIN'ini degistirir; degisiklik rapordaki plaintext'e yansir.
- Mevcut roller etkilenmez.

### Asama 3: Gider + Attachment + Mahsuplu Gider (normal kasa)

Durum: Tamamlandi.

Kapsam:

- Migration `AddMobileCoreTables`: Attachment, MahsupIslem.
- `ImageAttachmentService` (ImageSharp), `MahsupService` (ana kasa uzerinden).
- `/m/Gider` ekranlari (yonetici normal gider + foto; Sakin mahsup akisi), masaustu tekil silme korumasi.

Kabul kriterleri:

- Sakin foto cekip mahsuplu gider kaydeder -> aidat borcu duser, gider kategorisinde gorunur, ana kasa bakiyesi degismez (net sifir).
- Foto ~500KB altina sikisir ve tekrar acilir.
- Mahsup butun olarak silinebilir, parcali silinemez; tum adimlar audit'te ortak CorrelationId ile gorunur.
- Borcu asan tutar avans olusturur (tutarlilik kontrolleri temiz).

### Asama 4: Bildirim altyapisi (zil)

Durum: Tamamlandi.

Kapsam:

- Migration: Notification tablosu + `ServiceRequest.AssignedToUserId`.
- `NotificationService`, zil + rozet + polling, `/m/Bildirimler` ekrani.
- Atama UI'lari (mobil + masaustu Requests) kullanici secer, atamada bildirim.
- Mobil Talepler ve Duyurular ekranlari.

Kabul kriterleri:

- Talep atanan kullanici 60 sn icinde rozet gorur; bildirime tiklayinca talep detayina gider ve okundu olur.
- Sakin talep acabilir, erisimindeki daireler disindaki talepleri goremez.

Dogrulama notlari (uctan uca, gercek sunucuya karsi):

- Masaustu `Requests` Create/Edit: kullaniciya atama yapildiginda `Notification` satiri olusuyor, `AssignedTo` gorunen adi senkronize oluyor; atama degismeden tekrar kaydetmede yeni bildirim OLUSMUYOR (duplicate onlendi).
- `GET /m/Bildirimler/Ozet` dogru okunmamis sayisini donduruyor; `/m/Bildirimler/Ac/{id}` `LinkUrl`'e yonlendirip `ReadAt` alanini isaretliyor; `TumunuOku` tum bildirimleri tek seferde okunmus yapiyor.
- Mobil `Talepler/Guncelle`: personel durum + atama degistirebiliyor (degisen atamada bildirim), Sakin dogrudan POST denediginde `Forbid()` ile `AccessDenied`'a yonlendiriliyor ve talep durumu degismiyor.
- Zil rozeti gercek tarayicida (CDP ile headless Edge, gercek fetch/DOM) dogrulandi: `/m` sayfasi yuklendiginde rozet okunmamis sayisini gosteriyor, zile tiklayinca `/m/Bildirimler` listesine gidiyor.
- Test sirasinda bulunan ve dogrulanan onemli nokta: `dotnet run` KOMUTU `ASPNETCORE_ENVIRONMENT=Development` OLMADAN calistirilirsa (varsayilan Production'a duser), `MapStaticAssets` derlenmis/sikistirilmis statik dosya manifestini bulamiyor ve gzip Accept-Encoding gonderen gercek tarayicilara BOS govde donduruyor (tum `wwwroot` JS/CSS calismiyor). Bu sadece yayinlanmamis (`dotnet run`) test ortami sorunu; gercek dagitim `Dockerfile` icinde `dotnet publish` kullandigi icin (statik varlik sikistirma adimi orada calisiyor) bu sorun production'da olusmuyor. Yerel/manuel test yaparken mutlaka `ASPNETCORE_ENVIRONMENT=Development` set edilmeli.

### Asama 5: Web Push

Durum: Tamamlandi.

Kapsam:

- Migration: PushSubscription tablosu (`UserId`, `Endpoint` unique, `P256dh`, `Auth`, `UserAgent`, `CreatedAt`, `FailCount`; Notification ile ayni sebeple soft referans, FK yok).
- `Lib.Net.Http.WebPush` paketi (3.3.1), `PushSenderService` (VAPID `Push:Subject/PublicKey/PrivateKey` config; ucu bos ise `Enabled=false`, gonderim sessizce atlanir).
- `PushQueue` (unbounded `Channel<PushJob>`) + `PushDispatchHostedService` (arka planda tuketir, istegi bekletmez; 404/410 donen abonelik otomatik silinir).
- `NotificationService.NotifyAsync` her cagride push isini kuyruga ekler (best-effort; kuyruk/gonderim hatasi DB bildirimini etkilemez).
- `/m/Bildirimler/Abone` (POST, form-encoded: endpoint/p256dh/auth) ve `/m/Bildirimler/AbonelikSil` (POST, endpoint) uc noktalari; `wwwroot/js/mobile-push.js` (sadece Bildirimler sayfasinda yuklenir) `Notification.requestPermission()` + `pushManager.subscribe()` + form POST akisini yonetir.
- `wwwroot/sw.js` icinde `push` (payload JSON: title/body/url) ve `notificationclick` (ilgili pencereyi odaklar veya yeni pencere acar) handler'lari.
- Duyuru yayininda fan-out: `AnnouncementsController` artik `UserManager` + `NotificationService` enjekte eder; `Create` (IsPublished=true) ve `Edit` (yayinlanmamis -> yayinlanmis gecisi) tum kayitli kullanicilara (Sakin dahil) Duyuru tipi bildirim + push gonderir. Ayni duyuru tekrar duzenlenirse (zaten yayinda ise) yeniden bildirim GONDERILMEZ.
- Coolify env degiskenleri: `Push__Subject` (`mailto:...`), `Push__PublicKey`, `Push__PrivateKey` (VAPID anahtar cifti; appsettings.json'da bos deger, private key asla dosyaya yazilmaz).

Kabul kriterleri:

- Android Chrome'da izin veren kullanici, uygulama kapaliyken talep atamasinda sistem bildirimi alir.
- iOS 16.4+ ana ekran kurulumuyla calisir.
- Anahtar tanimsizsa uygulama hatasiz calisir (yalniz zil); 410 donen abonelik otomatik silinir.

Dogrulama notlari (uctan uca, gercek sunucuya karsi, test VAPID anahti uretilerek):

- Headless Edge (CDP) ile `Browser.grantPermissions` kullanilarak bildirim izni programatik verildi, `/m/Bildirimler` sayfasinda "Anlık bildirimleri aç" butonuna GERCEK mouse tiklamasiyla tiklandi; `pushManager.subscribe()` GERCEK bir WNS (Windows Notification Service) endpoint'i uretti ve bu `/m/Bildirimler/Abone` uzerinden `PushSubscriptions` tablosuna dogru kullanici ID'siyle kaydedildi.
- Ayni tarayici oturumu icinde (abonelik acikken) `Requests/Create` ile talep atamasi tetiklendi; `PushDispatchHostedService` kuyruktan isi aldi, VAPID ile imzali GERCEK bir push mesaji WNS'e gonderdi ve servis calisani (`sw.js`) `push` olayini alip `showNotification()` cagirdi — tarayicida `reg.getNotifications()` ile dogrulanan bildirim, atama basligi ve aciklamasiyla birebir eslesti (ekran goruntusu: `push_realtime.png`). Bu, ucu ucuna gercek bir push teslimati kanitidir (mock/simulasyon degil).
- Duyuru yayini: yeni duyuru `IsPublished=true` ile olusturulunca 4 kayitli kullanicinin (1 yonetici + 3 Sakin) hepsine Duyuru tipi bildirim gitti. Taslak (`IsPublished=false`) olusturulan duyuru bildirim GONDERMEDI; ayni duyuru sonradan yayina alininca (false->true gecisi) bildirim gonderildi; zaten yayinda olan duyuru tekrar duzenlenince YENIDEN bildirim gonderilmedi (spam onlendi).
- Abonelik silme (`AbonelikSil`) dogru `Endpoint` ile eslesen kaydi sildi; farkli bir `pushManager.subscribe()` cagrisi WNS'den farkli bir endpoint uretebiliyor (ayni cihaz/tarayicida bile), bu yuzden coklu abonelik satiri normal bir durum.

### Asama 6: Raporlar, Kasa-Banka ve cila

Durum: Baslamadi.

Kapsam:

- `/m/Raporlar` (Borclular, Alacaklilar, kategori filtreli giderler), `/m/KasaBanka`.
- `/m/Yardim/Kurulum` sayfasi, offline sayfasi cilasi, sw cache versiyonlama.
- `tests/` altinda `MahsupService`, `MobileScopeService` ve `ResidentAccountService` birim testleri.

Kabul kriterleri:

- Rapor rakamlari masaustu raporlarla birebir.
- Cevrimdisi acilista offline sayfasi gelir; testler yesil.

## Riskler ve Acik Sorular

- Sifre plaintext saklaniyor (sistem yoneticisi gorebilsin diye). Dusuk-hassasiyet 5 haneli PIN olsa da yedeklerde acik durur; gerekirse Data Protection ile sifreli saklamaya gecis acik kapi.
- SQLite dev ortami `EnsureCreated` kullaniyor: migration'lar dev'de kosmaz, sema degisikliginde `app.db` yeniden olusturulmali.
- .NET 10 RC paketleri: `SixLabors.ImageSharp` ve `Lib.Net.Http.WebPush` net10 ile test edilmeli; sorun cikarsa ImageSharp yerine SkiaSharp'a gecilebilir.
- ImageSharp 4.x ticari lisans gerektiriyor (Six Labors Split License) — proje **3.1.x** serisine sabitlenmis olmali (`Kumburgaz.Web.csproj`, su an 3.1.12). Paketi yukseltirken bu detaya dikkat edilmeli.
- EF Core soft-delete + Restrict FK kombinasyonu: ayni DbContext'te bir bagimlı entity (orn. MahsupIslem) tracked haldeyken referans verdigi principal (Collection/LedgerTransaction) `Remove()` ile isaretlenirse "association severed" hatasi alinir (soft-delete FK'yi hic sifirlamadigi icin). Cozum: bagimliyi once sil+kaydet, sonra `ChangeTracker`'dan `Detach` et (bkz. `MahsupService.DeleteAsync`).
- bytea buyumesi: fotograflar DB'de oldugundan yedek boyutu buyur; yedek suresi izlenmeli. `Attachment.EntityType/EntityId` yapisi ileride dosya sistemine tasimaya uygun.
- Eski `AssignedTo` string kayitlarinin `AssignedToUserId`'si bos kalir; bildirim yalnizca yeni atamalarda calisir (kabul edilebilir).
- iOS push icin ana ekran kurulumu sart; kullanici egitimi Kurulum sayfasiyla yapilir.
- Service worker HTML yanitlarini asla cache'lememeli (auth guvenligi); sw guncellemelerinde versiyonlu cache disiplini.
- `DuesPayerType` (kiraci oder) ile Sakin'in hesabi celisirse: `ResolveResponsibleAccountIdAsync` sonucu kullanilir, UI uyarir ama engellemez.
- Ana kasa birden fazlaysa mahsup icin ilk aktif kasa kullanilir; ihtiyac olursa Ayarlar'a varsayilan kasa secimi eklenir.
- Acik soru: duyuru push'u herkese mi gitsin? (Oneri: tum aktif kullanicilara; ayar sonradan eklenebilir.)

## Olusturulacak / Degistirilecek Dosyalar (ozet)

Yeni:

- `Areas/Mobile/**` (controller + view seti)
- `Services/MobileScopeService.cs`, `Services/SakinAreaRestrictionFilter.cs`, `Services/ResidentAccountService.cs`, `Services/MahsupService.cs`, `Services/ImageAttachmentService.cs`, `Services/NotificationService.cs`, `Services/PushSenderService.cs`, `Services/PushDispatchHostedService.cs`
- `wwwroot/manifest.webmanifest`, `wwwroot/sw.js`, `wwwroot/js/mobile.js`, `wwwroot/css/mobile.css`, `wwwroot/offline.html`, `wwwroot/img/icons/*`
- Migration'lar: `AddResidentAccounts`, `AddMobileCoreTables`, `AddNotifications`, `AddPushSubscriptions`
- `Models/MobileModels.cs`, `Models/MobileViewModels.cs`

Degisecek:

- `Program.cs` (route, DI, seed: Sakin rolu + RolePermission + SeedResidentAccountsAsync)
- `Models/DomainModels.cs` (AppRoles.Sakin, ServiceRequest.AssignedToUserId, Account.MobilePassword)
- `Data/ApplicationDbContext.cs` (DbSet'ler + index'ler)
- `Controllers/AccountsController.cs` (Create'te giris uret; Edit'te daire erisimi + sifre yonetimi)
- `Controllers/SystemUsersController.cs` (Sakin kullanicilarini gizle)
- `Controllers/ReportsController.cs` (Kullanici Giris Bilgileri raporu)
- `Controllers/RequestsController.cs` (kullanici secimli atama + bildirim)
- `Controllers/CollectionsController.cs`, `Controllers/LedgerController.cs` (mahsup tekil silme korumasi)
- `Controllers/AnnouncementsController.cs` (yayin bildirimi)
- `Kumburgaz.Web.csproj` (SixLabors.ImageSharp, Lib.Net.Http.WebPush)
