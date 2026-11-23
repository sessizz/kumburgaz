# Transfer aracı, gün içi sıralama ve PDF küçük resmi — Tasarım

**Tarih:** 2026-11-23
**Durum:** Onaylandı (kullanıcı: "uygun, aynı gün")

## Amaç ve neden

1. MCP ile kasa ↔ banka transferi kaydedilebilsin. ATM'den bankaya yatırılan para, ekranda
   olduğu gibi "kasadan bankaya transfer" olarak işlenmeli; bugün MCP'de transfer aracı yok.
2. Kasa, banka ve vadeli hesap hareketlerinin gün içindeki sırası sürükle-bırak ile elle
   düzeltilebilsin. Kayıt sırası bazen banka dökümündeki sıraya uymuyor; yürüyen bakiye de
   bu yüzden dökümle tutmuyor.
3. Gider eklerindeki PDF kutucuklarında ilk sayfa küçük resim olarak görünsün.

## Mevcut durum

- Gün içi sıra `Date` alanının saat kısmında tutuluyor. `CashBankController.BuildNextTransactionDateAsync`
  her yeni kayda o hesabın o günkü bir sonraki "sıra" değerini verir (`TransactionOrderTickStep = 10` tick).
  `CashBankDetailService` satırları `Date`, sonra `Id` ile sıralar; yürüyen bakiye, Excel dökümü ve
  MCP `cash_bank_transactions` bu sırayı kullanır.
- Bir transfer iki `LedgerTransaction` satırıdır (`IsTransfer`, `TransferIsIncoming`), aynı
  `Date`/`Amount`/`Description` ile yazılır. Düzenleme, silme, detay ekranındaki karşı hesap ve
  tutarlılık kontrolü iki tarafı bu üç alanın birebir eşitliğiyle eşleştirir
  (`CashBankController.FindTransferPair`, `CashBankDetailService.FindTransferPair`,
  `ConsistencyCheckService.CheckTransferPairsAsync`).
- Bu yüzden bir transfer tarafının sırası (saat kısmı) değişirse eşleşme kopar.
- Transfer oluşturma kodu controller'dadır (`CreateTransfer`, `UpdateTransferTransaction`, CSV
  içe aktarmadaki `AddTransferRowsAsync`).
- Gider ekleri artık türüne göre çiziliyor; PDF'ler ikon + dosya adı kutucuğu ve tıklayınca
  `wwwroot/js/attachment-viewer.js` ile aynı sayfada önizleme.

## Tasarım

### Transfer eş kimliği
- `LedgerTransaction.TransferGroupId` (`Guid?`, indeksli) eklenir. Yeni her transferin iki tarafı
  aynı kimliği taşır.
- Mevcut transferler açılışta bir kez, bugünkü eşleştirme kuralıyla eşlenip kimlik alır. Her
  satır en fazla bir kez eşlenir; eşi bulunamayan satır boş kalır ve loglanır. İşlem idempotent'tir.
- Eşleştirme kimlik varsa kimlikle yapılır, yoksa bugünkü kurala düşer (eşlenemeyen eski veri için).

### Transfer servisi ve MCP aracı
- Transfer oluşturma, güncelleme ve sıra değeri verme bir servise taşınır; ekran, CSV içe aktarma
  ve MCP aynı kodu kullanır.
- Kasa'dan bankaya giden transferin açıklama öneki "Bankaya yatır", diğerlerinin "Para transferi"
  olur (bugünkü kural).
- MCP aracı `record_transfer(fromAccountKey, toAccountKey, amount, date?, description?, confirm,
  preview_token, idempotency_key)`. Kasa/Banka yazma yetkisi ister. Diğer kayıt araçlarıyla aynı
  önizleme/onay ve tekrar koruması kurallarını izler. Kaynak ve hedef aynı olamaz; iki hesap da
  var ve aktif olmalı.

### Gün içi sıralama
- Kasa/Banka detay ekranında sürükle-bırak yalnız aynı gün içinde çalışır; tarih asla değişmez.
- Kaydedilen sıra yalnız o hesabın o günkü satırlarının saat kısmını (sıra değerini) yeniden
  dağıtır. Bir transferin bir hesaptaki sırası diğer hesaptaki sırasını etkilemez.
- Sunucu, istemciden o hesabın o günkü satırlarının tam listesini yeni sırasıyla ister; eksik,
  fazla ya da başka güne/hesaba ait satır içeren liste reddedilir.
- Arama ya da tür filtresi açıkken veya günün satırları sayfaya sığmıyorsa sürükleme kapalıdır.
- Kasa/Banka yazma yetkisi gerekir; değişiklik normal denetim kaydına düşer.
- Masaüstü ekranında yapılır; mobil ekran bu sürümde değişmez.

### PDF küçük resmi
- pdf.js projeye yerel dosya olarak eklenir (`wwwroot/lib`, sabit sürüm). Yalnız sayfada PDF
  kutucuğu görünürken yüklenir.
- İlk sayfa kutucuğun içine çizilir. Çizim başarısız olursa bugünkü ikon + dosya adı görünümü kalır.
- Masaüstü gider detayı, mobil gider detayı ve listedeki ataş önizlemesi için geçerlidir.

## Hedef dışı
- Bir satırı başka bir güne taşımak (tarihi değiştirmek için düzenleme kullanılır).
- Mobilde sürükle-bırak.
- Transfer silme/düzenleme MCP aracı.
