# Kumburgaz MCP Bağlayıcısı — Tasarım

**Tarih:** 2026-11-22
**Durum:** Uygulandı (2026-11-23, plan: `docs/superpowers/plans/2026-11-22-mcp-connector.md`). Uygulamadaki farklar için sondaki "Uygulama notları"na bakın.

## Amaç ve neden

Kumburgaz'ı Kiracı360 bağlayıcısı gibi claude.ai'den (web, masaüstü, telefon) kullanmak.
Sohbetle soru sormak ("A blok 5 kimin, borcu ne?", "bu ay ne tahsil ettik?", "kim ödemedi?")
ve onaylı kayıt girmek ("7 numaradan 2.500 TL havale geldi, işle").

Kullanıcı bağlayıcıyı claude.ai'de URL ile ekler (`https://kumburgaz.bahceliler.net/mcp`),
Kumburgaz giriş ekranında kendi hesabıyla oturum açar ve izni onaylar. Sonra Claude
kullanıcının yetkileriyle çalışır.

## Hedefler

- claude.ai özel bağlayıcısı olarak, ek sunucu kurmadan çalışmak.
- Kiracı360 ile aynı kalıp:
  - önce `search` ile kimlik bulma, sonra ayrıntı araçları
  - özet araçları
  - para işlemlerinde `confirm: false` ile önizleme, ardından `confirm: true` ile kayıt
  - oluşturma araçlarında `idempotency_key` ile tekrar koruması
  - yanıtlarda uygulamada açılacak `link`
  - Türkçe sunucu talimatları ve bir "rehber" kaynağı
- Mevcut rol/modül yetki matrisine (`PermissionService`) uymak. Claude, kullanıcının
  ekranda yapamayacağı hiçbir şeyi yapamaz.
- Claude'un yazdığı kayıtlar denetim kaydına (AuditLog) normal kullanıcı işlemi gibi
  ve kullanıcının adıyla düşer. Kaynak "MCP" olarak işaretlenir.

## Hedef dışı (bu sürümde yok)

- Silme ve düzenleme araçları. Yanlış kayıt uygulamadan düzeltilir.
- Sakin (malik/kiracı) rolünün bağlayıcı kullanması. Yalnız personel rolleri.
- Aidat üretimi, CSV içe aktarma, yedek, kullanıcı ve yetki yönetimi.
- Belge/fotoğraf yükleme.

## Kaçınılacaklar (çalışsa bile başarısızlık sayılır)

- Kimlik doğrulamasız ya da paylaşılan sabit anahtarla erişim.
- Claude'un onay almadan para kaydı açabilmesi (önizleme adımı atlanabilir olmamalı).
- Tutarların ekrandakinden farklı hesaplanması. Araçlar mevcut servisleri kullanır,
  hesaplamayı yeniden yazmaz.

## Mevcut durum

- ASP.NET Core 10 MVC, Identity (cookie), PostgreSQL. Coolify, Traefik ve Cloudflare
  Tunnel arkasında çalışıyor.
- Okuma mantığının çoğu servislerde: `IReportingService.GetDuesDebtReportAsync`,
  `UnitStatementService`, `CashBankDetailService.BuildAsync`, `UnitLedgerService`,
  `ICollectionService`.
- Yazma tarafında tahsilat `ICollectionService.CreateAsync` ile servis üzerinden yapılıyor.
  **Gelir/gider kaydı ise controller içinde** (`LedgerController.Create`,
  `CashBankController` gelir uçları). MCP için bu mantık bir servise taşınmalı ve hem
  ekran hem MCP o servisi kullanmalı.
- Yetki: `ModuleAuthorize` + `PermissionService.HasAccess(user, module, needWrite)`.

## Yaklaşımlar

**A. Uygulama içinde OAuth sunucusu (OpenIddict) ve `/mcp` ucu. Seçilen.**
- Resmi C# MCP SDK'sı (`ModelContextProtocol.AspNetCore`) aynı uygulamaya eklenir.
- OpenIddict 7 mevcut Identity kullanıcılarıyla yetkilendirme sunucusu olur:
  authorization code + PKCE ve refresh token.
- Dinamik istemci kaydı (DCR) için küçük bir uç yazılır. Yalnız Claude'un dönüş adreslerine
  izin verir (`https://claude.ai/api/mcp/auth_callback`, `https://claude.com/api/mcp/auth_callback`).
- Artı: Kiracı360 gibi sadece URL yapıştırılır. Tek uygulama, tek deploy, aynı veritabanı.
- Eksi: OAuth parçası işin en büyük kısmı. OpenIddict DCR'yi hazır sunmadığı için o uç
  bize ait.

**A'. A ile aynı, DCR yerine elle kayıtlı istemci.**
- claude.ai'nin "Gelişmiş ayarlar"ına bir kez client ID/secret girilir.
- Daha az kod ve daha az saldırı yüzeyi. Bağlantı başına bir adım fazla.
- DCR'de sorun çıkarsa geri dönüş yolu olarak A'nın içinde kalır.

**B. Elle yazılmış minimal OAuth.** Elendi. Güvenlik kritik kodu sıfırdan yazmak,
yerleşik bir kütüphaneye göre gereksiz risk.

**C. Sabit erişim anahtarı (header).** Elendi. claude.ai bağlayıcıları sabit header
desteklemiyor; yalnız Claude Code'da çalışır.

**D. Ayrı MCP servisi.** Elendi. Uygulamanın REST API'si yok; ikinci servis, ikinci deploy
ve ikinci yetki katmanı demek.

## Tasarım

### Kimlik ve yetki akışı

1. claude.ai `/mcp`'ye token'sız gelir. Sunucu `401` ve
   `WWW-Authenticate: Bearer resource_metadata=".../.well-known/oauth-protected-resource"` döner.
2. Korumalı kaynak metadatası (RFC 9728) yetkilendirme sunucusunu gösterir. Aynı uygulama
   `/.well-known/oauth-authorization-server` yayınlar (`registration_endpoint` dahil).
3. claude.ai DCR ile istemci açar. Yalnız izinli dönüş adresleri kabul edilir. Kayıtlar
   `OpenIddictApplications` tablosunda tutulur ve hız sınırlıdır.
4. `/connect/authorize`: kullanıcı mevcut giriş ekranına yönlenir.
   - Sakin rolü ve hiç rolü olmayan kullanıcı reddedilir.
   - Onay ekranı istemci adını ve "Claude, sizin yetkilerinizle okuma ve onaylı kayıt
     yapabilecek" metnini gösterir.
5. `/connect/token` PKCE zorunlu. Access token 1 saat, refresh token 30 gün (kayan).
6. `/mcp` isteğinde token doğrulanır. Principal kullanıcının id ve rol claim'lerini taşır.
   Her araç `PermissionService.HasAccess(user, module, needWrite)` ile modül kontrolü
   yapar. Rol değişirse en geç token yenilemede etkili olur.
7. Kullanıcı "Bağlı uygulamalar" ekranından bağlantıyı iptal edebilir; yönetici tüm
   kullanıcılarınkini görür.

### Araçlar (ilk sürüm)

Okuma (modül görüntüleme yetkisi):

| Araç | Ne döner | Kaynak |
|---|---|---|
| `site_summary` | Kasa/banka bakiyeleri, toplam ve gecikmiş aidat borcu, bu ay tahsilat, bu ay gider | Panel hesapları |
| `search` | Daire (blok + no), malik/kiracı adı, telefon; aksan duyarsız | Units, Accounts |
| `get_unit` | Daire: kişiler, aidat grubu, bakiye, açık taksitler, son tahsilatlar, link | UnitLedgerService |
| `unit_statement` | Daire hesap ekstresi (tarih aralığı) | UnitStatementService |
| `overdue_dues` | "Kim ödemedi": borçlu daireler, en çok borçlu önce; dönem/blok filtresi | ReportingService.GetDuesDebtReportAsync |
| `collections` | Tahsilatlar: tarih aralığı, daire, kanal | Collections |
| `cash_bank_accounts` | Kasa ve banka hesapları ile bakiyeleri | CashBankBalanceHelper |
| `cash_bank_transactions` | Hesap hareketleri ve yürüyen bakiye | CashBankDetailService |
| `ledger` | Gelir/gider hareketleri: kategori ve tarih filtresi | LedgerTransactions |
| `categories` | Gelir/gider kategorileri (yazma araçları için id) | IncomeExpenseCategories |

Yazma (modül yazma yetkisi, `confirm` + `idempotency_key`):

| Araç | Ne yapar | Kaynak |
|---|---|---|
| `record_collection` | Daireye tahsilat: tutar, tarih, kasa/banka, dönem ya da devir, makbuz | ICollectionService.CreateAsync |
| `create_expense` | Gider: kategori, tutar, tarih, kasa/banka, açıklama | yeni LedgerEntryService |
| `create_income` | Aidat dışı gelir | yeni LedgerEntryService |

Kurallar:
- Tutarlar sayı ya da Türkçe metin olabilir (`"2.500,00"`); `FlexibleDecimalParser` ile
  okunur. Tarihler `YYYY-AA-GG`.
- `confirm: false` hiçbir şey yazmaz. Neyin, hangi daireye/hesaba, hangi tutarla
  yazılacağını, tahsilatın hangi taksitlere dağılacağını ve kalan borcu döner.
- `confirm: true` aynı girdilerle kaydeder. `idempotency_key` 24 saat saklanır; aynı
  anahtarla ikinci çağrı ilk sonucu döner.
- Sonuçlar kayıt id'si ve `link` taşır, örneğin
  `https://kumburgaz.bahceliler.net/Units/Detail/12`.
- Liste araçlarında varsayılan sınır 100 satır, en fazla 500.

### Sunucu talimatları ve rehber

Kiracı360'taki gibi Türkçe kısa talimatlar:
- para biçimi `₺2.500,00`, tarih `GG.AA.YYYY`
- önce `search`; birden çok eşleşmede tahmin etme, sor
- "kim ödemedi" sorusu için `overdue_dues`
- para işlemlerinde önce önizleme, sonra kullanıcıdan onay

`kumburgaz://rehber` kaynağı kavramları anlatır: aidat grubu, birleşik daire, devir bakiyesi,
avans, mahsup, kasa/banka anahtarları.

### Kod yerleşimi

- `Mcp/`: araç sınıfları (`[McpServerToolType]`), sunucu talimatları, rehber kaynağı.
- `Auth/`: OpenIddict yapılandırması, `AuthorizationController` (authorize/token/onay),
  DCR ucu, "Bağlı uygulamalar" ekranı.
- `Services/LedgerEntryService.cs`: LedgerController ve CashBankController'daki gelir/gider
  yazma mantığı buraya taşınır; ekranlar da bunu kullanır.
- Yeni tablolar: OpenIddict (EF Core) ve `McpIdempotencyKeys`. Tek migration.

### Test

- Entegrasyon (mevcut `KumburgazAppFactory`):
  - metadata uçları
  - DCR (izinli/izinsiz dönüş adresi)
  - authorize → token (PKCE), token'sız `/mcp` için 401
  - Sakin kullanıcının reddi
- MCP C# istemcisiyle uçtan uca: `tools/list`, okuma aracı ve yazma aracının önizleme/kayıt
  ve idempotency davranışı.
- Araç birim testleri (InMemory DB): yetkisiz modülde ret, önizlemenin veritabanına
  dokunmaması, tutarların ekrandaki servisle aynı çıkması.

### Dağıtım

- Ek altyapı yok. Coolify'da yeni ortam değişkeni: OpenIddict imzalama/şifreleme
  sertifikaları için bir anahtar. Yoksa ilk açılışta üretilip veritabanında saklanır
  (Data Protection'daki gibi deploy'da kaybolmaz).
- Cloudflare tarafında değişiklik yok; `/mcp` ve `/.well-known/*` aynı hostname'de.

## Builder'a bırakılanlar

- Araç adlarının ve alanlarının son hali, `site_summary`'deki alanların tam listesi.
- Onay ekranının görünümü (mevcut Login sayfasıyla aynı stil).
- DCR hız sınırı değerleri.

## Benim kararlarım (düzeltilebilir)

- Yazma araçları ilk sürümde yalnız tahsilat, gider ve gelir.
- Sakin rolü bağlayıcıyı kullanamaz.
- Token süreleri: 1 saat / 30 gün.
- DCR yalnız Claude dönüş adreslerine açık; elle kayıtlı istemci de desteklenir.

## Uygulama notları (2026-11-23)

### Bağlanma
claude.ai → Ayarlar → Bağlayıcılar → Özel bağlayıcı ekle → `https://kumburgaz.bahceliler.net/mcp`.
Kumburgaz hesabıyla giriş yapılır ve onay verilir. Bağlantılar "Bağlı uygulamalar" ekranından
(Ayarlar'ın yanında) görülür ve iptal edilir; iptal anında etkilidir.

### Tasarımdan farklar ve eklenen kurallar
- Bağlayıcıyı Sakin rolü taşıyan hiçbir kullanıcı kullanamaz (personel rolü olsa bile).
- Kullanıcının uygunluğu (var, kilitli değil, uygun rol, Sakin değil, güvenlik damgası aynı)
  her `/mcp` isteğinde veritabanından kontrol edilir. Şifre değişince (ve kullanıcı adı, e-posta
  ya da telefonunu kendisi değiştirince) Claude bağlantısı kopar ve yeniden bağlanmak gerekir.
- Kayıt araçlarında önizleme zorunludur: `confirm: false` bir `previewToken` döner (30 dakika,
  tek kullanımlık). `confirm: true` bu token ve bir `idempotency_key` ister. Bir önizleme en fazla
  bir kayıt açar. Aynı anahtarla tekrar gelen istek ilk sonucu döner; anahtarın farklı bir işlemle
  kullanılması reddedilir.
- Tahsilat yazılırken dairenin aidat grubu satırları kilitlenir. Bu, aynı anda gelen iki Claude
  kaydının borcu iki kez kapatmasını önler.
- Liste araçlarında `total` TL toplamı, `count` satır sayısıdır (limit uygulanmadan önce).
- `site_summary` Panel ekranıyla aynı formülleri kullanır (UTC takvim ayı).
  `accounts` yalnız Kasa/Banka, `duesDebtTotal` yalnız Raporlar yetkisiyle döner.
- Telefon numarası yalnız Daireler veya Hesaplar, `unit_statement` yalnız Daireler yetkisiyle
  görünür. Daire linkleri Daireler yetkisi olmayana boş döner.
- Gelir/gider kaydı `LedgerEntryService`'e, tahsilat dağıtım planı
  `ICollectionService.PlanAllocationAsync`'e, Panel hesapları `DashboardFiguresService`'e taşındı.
  Ekranlar da bu servisleri kullanır.
- OAuth anahtarları, token'lar ve tekrar koruma kayıtları denetim kaydına yazılmaz.

### Bilinen ve bırakılan konular
- Eski OpenIddict token ve istemci kayıtlarını temizleyen zamanlanmış iş yok.
- Açılışta geçici bir Data Protection hatası, OAuth imza anahtarının bir kez değişmesine
  yol açabilir. Etkisi bir kerelik yeniden giriştir; olay Critical olarak loglanır.
- `get_unit` açık taksit kalanı dağıtılmamış avansı hesaba katmaz (tahsilat formuyla aynı,
  Aidatlar ekranından farklı). `overdue_dues.oldestPeriod` devir alacağını hesaba katmaz.
