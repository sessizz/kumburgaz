# Transfer Aracı, Gün İçi Sıralama ve PDF Küçük Resmi — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** MCP'ye kasa↔banka transfer aracı eklemek, kasa/banka hareketlerinin gün içi sırasını sürükle-bırak ile düzeltilebilir yapmak ve gider eklerindeki PDF'lere ilk sayfa küçük resmi koymak.

**Architecture:**
- Transferlerin iki tarafı kalıcı bir eş kimliğiyle (`TransferGroupId`) bağlanır. Böylece bir tarafın gün içi sıra değeri değişse de eşleşme kopmaz.
- Transfer oluşturma ve sıra değeri verme controller'dan servislere taşınır; ekran, CSV içe aktarma ve MCP aynı kodu kullanır.
- Sürükle-bırak yalnız o hesabın o günkü satırlarının `Date` saat kısmını (sıra değeri) yeniden dağıtır.
- PDF küçük resmi istemcide pdf.js ile çizilir.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10 (Npgsql / testte SQLite + InMemory), ModelContextProtocol.AspNetCore 2.4.0, xUnit, pdf.js (pdfjs-dist, yerel dosya), SortableJS (yerel dosya).

**Spec:** `docs/superpowers/specs/2026-11-23-transfer-order-pdf-design.md`

## Global Constraints

- Gün içi sıra bugünkü gibi `Date`'in saat kısmındadır: sıra değeri `slot`, `Date = gün + slot * 10 tick` (`TransactionOrderTickStep = 10`). Tarihin gün kısmı sıralamada hiçbir zaman değişmez.
- Sürükle-bırak yalnız aynı gün içinde çalışır. Bir hesabın sırası diğer hesabın sırasını etkilemez.
- Transfer açıklama öneki: kasa → banka için `"Bankaya yatır"`, diğer yönler için `"Para transferi"`. Açıklama `"{önek}: {açıklama}"`, açıklama boşsa yalnız önek.
- Eşleştirme: `TransferGroupId` varsa yalnız ona göre yapılır. Yoksa bugünkü kural uygulanır (aynı `Date`, `Amount`, `Description`, ters yön, en yakın `Id`).
- MCP `record_transfer` aracı mevcut kayıt araçlarının kurallarına uyar:
  - modül KasaBanka, yazma yetkisi
  - `confirm: false` önizleme döner ve `previewToken` verir; token tek kullanımlıktır
  - `confirm: true` için `preview_token` ve `idempotency_key` (8–100 karakter) zorunludur
  - tutar en fazla 2 ondalık basamak ve en fazla 999.999.999 olabilir
  - Türkçe `McpException` mesajları
- Kasa/Banka yazma işlemleri `ModuleAuthorize(AppModules.KasaBanka)` ile korunur; POST'lar antiforgery ister.
- Yeni JS kütüphaneleri `wwwroot/lib/<ad>/` altına sabit sürümle konur ve CDN'den yüklenmez.
- Mevcut testlerin hepsi geçmeye devam eder. Dosyalar düzenlenirken satır sonları korunur (Edit aracı).

## Review Focus

1. **Eski transferlerin eşlenmesi.** Aynı gün aynı tutar ve açıklamayla iki transfer varsa, eşleme her tarafı bir kez kullanmalı ve iki ayrı çift oluşturmalı. Test: Task 1 `Backfill_pairs_two_identical_transfers_into_two_groups`.
2. **Sıra değiştikten sonra transferi düzenleme/silme.** Bir tarafı sürüklendikten sonra transfer silinince iki taraf da silinmeli; düzenlenince iki taraf da güncellenmeli. Test: Task 3 `Transfer_edit_and_delete_still_find_the_other_leg_after_reorder`.
3. **Eksik ya da yabancı satırla sıralama isteği.** Gönderilen liste o günün tam satır kümesi değilse hiçbir şey değişmemeli. Test: Task 3 `Reorder_rejects_partial_or_foreign_rows_and_changes_nothing`.
4. **Gün sınırı.** Sıralanan satırın yürüyen bakiyesi değişmeli ama tarihi (gün) aynı kalmalı. Excel dökümü ve MCP `cash_bank_transactions` yeni sırayı göstermeli. Test: Task 3 `Reorder_changes_running_balance_order_but_not_the_day`.
5. **Bozuk ya da şifreli PDF.** Küçük resim çizilemezse kutucuk ikon + dosya adıyla kalmalı ve tıklayınca yine açılmalı. Test: Task 6, JS'te try/catch yolu ve görünüm testinde yedek içerik.

---

### Task 1: Transfer eş kimliği ve eski transferlerin eşlenmesi

**Files:**
- Modify: `Models/DomainModels.cs` (`LedgerTransaction`), `Data/ApplicationDbContext.cs` (indeks), `Program.cs` (açılışta eşleme)
- Create: `Services/TransferPairing.cs`, `Data/Migrations/*_AddTransferGroupId.cs`
- Modify (eşleştirmeyi kimliğe taşı): `Controllers/CashBankController.cs` (`FindTransferPair` ve 3 çağıranı), `Services/CashBankDetailService.cs` (`FindTransferPair`), `Services/ConsistencyCheckService.cs` (`CheckTransferPairsAsync`)
- Test: `tests/Kumburgaz.Web.Tests/TransferPairingTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  // LedgerTransaction
  public Guid? TransferGroupId { get; set; }   // indeksli
  public static class TransferPairing
  {
      // Eşi olmayan transfer taraflarını bugünkü kuralla eşler, ortak Guid yazar; eşlenen çift sayısını döner. İdempotent.
      static Task<int> BackfillAsync(ApplicationDbContext db, ILogger logger, CancellationToken ct = default);
      // Kimlik varsa kimlikle, yoksa bugünkü kuralla karşı tarafı bulur.
      static Task<LedgerTransaction?> FindPairAsync(ApplicationDbContext db, LedgerTransaction leg, CancellationToken ct = default);
  }
  ```
  "Transfer tarafı" tanımı bugünkü `CashBankController.IsTransferLedger` ile aynıdır (`IsTransfer` ya da kategori adında "Transfer"). Kuralın tek kopyası `TransferPairing`'de olur; iki `FindTransferPair` kopyası ve tutarlılık kontrolü onu kullanır.

- [ ] **Step 1: Failing testler** (InMemory, mevcut `CreateDb()` kalıbı):
  - `Backfill_pairs_legs_that_match_today_rule`: aynı Date/Amount/Description, ters yönlü iki satır → ikisi de aynı `TransferGroupId`; dönüş 1.
  - `Backfill_pairs_two_identical_transfers_into_two_groups` (Review Focus 1): iki eş çift → 2 farklı kimlik; her kimlik tam 2 satırda; dönüş 2.
  - `Backfill_is_idempotent`: ikinci çağrı 0 döner, kimlikler değişmez.
  - `Backfill_leaves_unmatched_leg_null`.
  - `FindPair_uses_group_id_even_when_dates_differ`: aynı kimlikli, `Date` saat kısmı farklı iki taraf → bulunur.
  - `FindPair_falls_back_to_legacy_rule_without_group_id`.
- [ ] **Step 2:** `dotnet test --filter TransferPairingTests` → derleme hatası.
- [ ] **Step 3:** Alanı, indeksi ve servisi yaz. `dotnet ef migrations add AddTransferGroupId` çalıştır. `Program.cs`'te `CollectionAdvanceAllocator.ApplyAsync`'in yanında `await TransferPairing.BackfillAsync(db, app.Logger)` çağır. Üç eşleştirme yerini `TransferPairing.FindPairAsync`'e bağla. Mevcut davranış değişmez; aynı-tarih kuralı yalnız kimliksiz veride kullanılır.
- [ ] **Step 4:** `dotnet test` → hepsi geçer. `dotnet ef migrations has-pending-model-changes` → değişiklik yok.
- [ ] **Step 5:** Commit: `feat: transferlerin iki tarafına kalıcı eş kimliği (TransferGroupId)`

### Task 2: Transfer ve sıra servisleri

**Files:**
- Create: `Services/TransactionOrderService.cs`, `Services/CashBankTransferService.cs`
- Modify: `Controllers/CashBankController.cs` (`CreateTransfer`, `UpdateTransferTransaction`, `AddTransferRowsAsync`, `BuildNextTransactionDateAsync` ve ilgili yardımcılar servise taşınır), `Program.cs` (DI)
- Test: `tests/Kumburgaz.Web.Tests/CashBankTransferServiceTests.cs`

**Interfaces:**
- Consumes: Task 1 `TransferGroupId`, `TransferPairing.FindPairAsync`.
- Produces:
  ```csharp
  public class TransactionOrderService(ApplicationDbContext db)
  {
      public const long TickStep = 10;
      Task<DateTime> NextSlotAsync(string accountKey, DateTime date);   // UTC; o hesabın o günkü en büyük sırasının bir fazlası
  }
  public sealed record TransferRequest(string FromAccountKey, string ToAccountKey, decimal Amount, DateTime Date, string? Description);
  public sealed record TransferPreview(string FromAccountKey, string FromAccountName, string ToAccountKey, string ToAccountName, decimal Amount, DateTime Date, string Description);
  public sealed record TransferResult(int OutgoingId, int IncomingId, Guid GroupId);
  public class CashBankTransferService(ApplicationDbContext db, TransactionOrderService order)
  {
      Task<TransferPreview> PreviewAsync(TransferRequest request);   // yazmaz
      Task<TransferResult> CreateAsync(TransferRequest request);     // iki taraf, ortak GroupId, kaynak hesabın sıra değeri
      Task UpdateAsync(int legId, TransferRequest request);          // iki tarafı birlikte günceller
  }
  ```
  Hatalar `InvalidOperationException` ile, Türkçe mesajla verilir:
  - `"Kaynak ve hedef hesap aynı olamaz."`
  - `"Geçerli bir kasa/banka hesabı seçiniz."` (anahtar çözülemez, hesap yok ya da pasif)
  - `"Tutar sıfırdan büyük olmalı."`
  - `"Transferin karşı satırı bulunamadı."`

  Hesap anahtarı biçimi `cash:{id}` / `bank:{id}` (`FinancialAccountHelper`).

- [ ] **Step 1: Failing testler**
  - `Create_writes_two_legs_with_shared_group_id_and_prefix`: kasa→banka 51.600 → iki satır; `IsTransfer`; giden `TransferIsIncoming=false` ve kasada, gelen `true` ve bankada; aynı `GroupId`; açıklama `"Bankaya yatır: ATM"`.
  - `Create_uses_para_transferi_prefix_for_bank_to_cash`.
  - `Create_takes_next_slot_of_source_account_for_the_day`: o gün kasada 2 kayıt varken yeni transferin `Date` saat kısmı `3 * 10` tick.
  - `Preview_writes_nothing`.
  - `Create_rejects_same_account`, `Create_rejects_inactive_account`.
  - `Update_changes_both_legs`.
- [ ] **Step 2:** Kırmızı.
- [ ] **Step 3:** Servisleri yaz. Controller'daki eylemleri bunları çağıracak şekilde değiştir: hata mesajları `TempData["ActionError"]`'a, yönlendirmeler bugünkü gibi. CSV içe aktarmadaki transfer satırları da `CreateAsync`'i kullanır. Diğer eylemlerin sıra değeri için `TransactionOrderService.NextSlotAsync`'i kullan (aynı değer).
- [ ] **Step 4:** `dotnet test` → hepsi geçer (mevcut `CashBankControllerTests` dahil).
- [ ] **Step 5:** Commit: `refactor: transfer oluşturma ve gün içi sıra değerini servislere taşı`

### Task 3: Gün içi yeniden sıralama (sunucu)

**Files:**
- Modify: `Services/TransactionOrderService.cs`, `Controllers/CashBankController.cs` (yeni eylem)
- Test: `tests/Kumburgaz.Web.Tests/TransactionReorderTests.cs`, `tests/Kumburgaz.Web.Tests/Integration/CashBankReorderEndpointTests.cs`

**Interfaces:**
- Consumes: Task 2 `TransactionOrderService`, Task 1 `TransferPairing`.
- Produces:
  ```csharp
  public sealed record TxRef(string Source, int Id);   // Source: "collection" | "ledger" (TxRow.Source ile aynı)
  // TransactionOrderService
  Task ReorderDayAsync(string accountKey, DateOnly day, IReadOnlyList<TxRef> oldestFirst);
  ```
  - Liste, o hesabın o güne ait tahsilat ve defter satırlarının tam kümesi olmalı. Değilse `InvalidOperationException("Sıralama listesi bu günün işlemleriyle uyuşmuyor; sayfayı yenileyin.")` atılır ve hiçbir şey yazılmaz.
  - Satırlara sırayla `slot = 1..n` verilir; `Date = gün + slot * TickStep`.
  - Tek `SaveChanges`.

  Uç: `POST /CashBank/ReorderDay`, form alanları `kind`, `id`, `day` (`yyyy-MM-dd`), `rows` (`"source:id"` dizisi, eskiden yeniye). Antiforgery zorunlu. Başarıda 204, hatada 400 ve JSON `{ error }` döner.

- [ ] **Step 1: Failing testler**
  - `Reorder_changes_running_balance_order_but_not_the_day` (Review Focus 4): `CashBankDetailService.BuildAsync` sırası yeni sırayla aynı; satırların `Date.Date` değişmez.
  - `Reorder_rejects_partial_or_foreign_rows_and_changes_nothing` (Review Focus 3): eksik satır, fazla satır, başka güne ait satır ve başka hesaba ait satır → hata; `Date`'ler aynı kalır.
  - `Reorder_of_one_transfer_leg_leaves_other_account_untouched`.
  - `Transfer_edit_and_delete_still_find_the_other_leg_after_reorder` (Review Focus 2): sıralamadan sonra `CashBankTransferService.UpdateAsync` iki tarafı günceller. Controller'ın silme yolu iki tarafı da siler.
  - Entegrasyon:
    - `ReorderDay_requires_kasabanka_write`: Personel → 403.
    - `ReorderDay_without_antiforgery_is_rejected`.
    - `ReorderDay_persists_order`.
- [ ] **Step 2:** Kırmızı. **Step 3:** Yaz. **Step 4:** `dotnet test` → hepsi geçer.
- [ ] **Step 5:** Commit: `feat: kasa/banka hareketlerini gün içinde yeniden sıralama ucu`

### Task 4: Sürükle-bırak arayüzü

**Files:**
- Create: `wwwroot/lib/sortablejs/Sortable.min.js` (SortableJS 1.15.6, npm paketinden; dosya başındaki lisans yorumu korunur), `wwwroot/js/cashbank-reorder.js`
- Modify: `Views/CashBank/_DetailParts/_TransactionsCard.cshtml`, `Views/CashBank/_DetailParts/_TxRow.cshtml`, `Views/CashBank/Detail.cshtml` (scriptler), `Services/CashBankDetailService.cs` (sürükleme açık mı bilgisi)
- Test: `tests/Kumburgaz.Web.Tests/Integration/CashBankReorderViewTests.cs`

**Interfaces:**
- Consumes: Task 3 `POST /CashBank/ReorderDay`.
- Produces:
  - `CashBankDetailViewModel` → `bool CanReorder`. Doğru olması için kullanıcının KasaBanka yazma yetkisi olmalı, arama boş olmalı ve tür filtresi `all` olmalı.
  - `TxDayGroup` → `bool IsCompleteOnPage`: günün bütün satırları bu sayfada mı.

Görünüm davranışı:
- Satırlar bugünkü gibi düz liste olarak çizilir. Her gün grubu `data-reorder-day="yyyy-MM-dd"` taşıyan ayrı bir `<ul>` içine alınır.
- Her satır `data-tx="source:id"` taşır. Satır başında yalnız `CanReorder && IsCompleteOnPage` iken görünen bir tutamaç (`drag_indicator`) bulunur.
- Ekran yeniden eskiye sıralı gösterir. JS gönderirken listeyi ters çevirip eskiden yeniye yollar.
- Başarıda sayfa yenilenir; hatada `alert` yerine sayfadaki mevcut hata alanına mesaj yazılır ve satır eski yerine döner.
- Toplu seçim modundayken sürükleme kapalıdır.

- [ ] **Step 1: Failing testler** (entegrasyon, rendered HTML):
  - `Detail_marks_days_and_rows_for_reorder_when_allowed`: `data-reorder-day` ve `data-tx="ledger:{id}"` var, tutamaç var.
  - `Detail_hides_reorder_when_searching_or_filtered`: `?Q=x` ya da `?Type=cikis` → tutamaç yok.
  - `Detail_hides_reorder_for_read_only_user`.
  - `Detail_loads_sortable_and_reorder_scripts`.
- [ ] **Step 2:** Kırmızı. **Step 3:** Yaz. `node --check wwwroot/js/cashbank-reorder.js` temiz olmalı. **Step 4:** `dotnet test` → hepsi geçer.
- [ ] **Step 5:** Commit: `feat: kasa/banka detayında gün içi sürükle-bırak sıralama`

### Task 5: MCP `record_transfer` aracı

**Files:**
- Modify: `Mcp/Tools/WriteTools.cs` (ya da yeni `Mcp/Tools/TransferTools.cs`; kayıt araçlarının önizleme/token/idempotency yardımcıları paylaşılır), `Mcp/McpSetup.cs`, `Mcp/McpInstructions.cs` (araç listesine bir ifade), `Mcp/McpGuideResource.cs` (transfer kavramı)
- Test: `tests/Kumburgaz.Web.Tests/McpTransferToolTests.cs`, `tests/Kumburgaz.Web.Tests/Integration/McpTransferFlowTests.cs`

**Interfaces:**
- Consumes: Task 2 `CashBankTransferService.PreviewAsync/CreateAsync`; mevcut `McpPreviewTokens`, `McpIdempotencyStore`, `McpInput`, `McpAccess`, `McpLinks.CashBank`.
- Produces: araç `record_transfer(fromAccountKey, toAccountKey, amount, date?, description?, confirm = false, preview_token?, idempotency_key?)` (`Destructive = false`, `Idempotent = false`).
  - Önizleme: `{ willWrite: false, summary, from: { key, name }, to: { key, name }, amount, date, description, previewToken }`.
  - Kayıt: `{ created: true, outgoingId, incomingId, links: { from, to } }`.
  - `InvalidOperationException` mesajları `McpException` olur.

- [ ] **Step 1: Failing testler**
  - `Preview_writes_nothing_and_returns_token`.
  - `Confirm_creates_two_legs_once_per_key_and_once_per_preview`: aynı anahtarla tekrar → aynı sonuç; aynı token farklı anahtarla → ilk sonuç; tek çift.
  - `Atm_deposit_from_cash_to_bank_uses_bankaya_yatir_prefix`.
  - `Requires_kasabanka_write`.
  - `Rejects_same_account_and_inactive_account`.
  - Entegrasyon `Transfer_via_mcp_shows_in_both_accounts`: MCP istemcisiyle önizleme → onay; ardından `cash_bank_transactions` iki hesapta da satırı gösterir.
- [ ] **Step 2:** Kırmızı. **Step 3:** Yaz. **Step 4:** `dotnet test` → hepsi geçer.
- [ ] **Step 5:** Commit: `feat: MCP record_transfer aracı (kasa↔banka transferi)`

### Task 6: PDF küçük resmi

**Files:**
- Create: `wwwroot/lib/pdfjs/pdf.min.mjs`, `wwwroot/lib/pdfjs/pdf.worker.min.mjs` (pdfjs-dist 4.10.38, npm paketinin `build/` klasöründen; lisans yorumu korunur)
- Modify: `wwwroot/js/attachment-viewer.js`, `Views/Ledger/Index.cshtml`, `Areas/Mobile/Views/Gider/Detay.cshtml`
- Test: `tests/Kumburgaz.Web.Tests/Integration/ExpenseAttachmentViewTests.cs`

**Interfaces:**
- Consumes: mevcut `data-attachment-url/-type/-name` kutucukları.
- Produces: PDF kutucukları `data-pdf-thumb` taşır ve içinde bir `<canvas>` ile yedek ikon + dosya adı bulunur.
  - `attachment-viewer.js`, sayfada `[data-pdf-thumb]` varsa pdf.js'i dinamik `import()` ile `/lib/pdfjs/pdf.min.mjs`'ten yükler (worker: `/lib/pdfjs/pdf.worker.min.mjs`).
  - Kutucuk görünür olunca (IntersectionObserver; masaüstü modal açılınca da) dosyayı `fetch` eder, ilk sayfayı kutucuğa sığacak ölçekte canvas'a çizer, sonra yedek ikonu gizler.
  - Hata olursa yedek görünüm kalır (Review Focus 5).
  - Listedeki ataş önizlemesi ilk ek PDF ise aynı küçük resmi kullanır.

- [ ] **Step 1: Failing testler**
  - `Desktop_pdf_tile_has_thumbnail_canvas_and_fallback`: `data-pdf-thumb`, `<canvas` ve dosya adı aynı kutucukta.
  - `Mobile_pdf_tile_has_thumbnail_canvas_and_fallback`.
  - `Pdfjs_files_are_served_locally`: `/lib/pdfjs/pdf.min.mjs` ve worker 200 döner.
- [ ] **Step 2:** Kırmızı. **Step 3:** Yaz. `node --check` ile JS'i kontrol et. **Step 4:** `dotnet test` → hepsi geçer.
- [ ] **Step 5:** Commit: `feat: gider eklerinde PDF ilk sayfa küçük resmi`

### Task 7: Canlıya alma

- [ ] Tüm testler geçer. `master`'a merge ve push edilir (kullanıcı onayı bu iş için önceden verildi). Coolify deploy'u izlenir.
- [ ] Canlı kontrol:
  - açılış logunda `AddTransferGroupId` migration'ı ve eşlenen transfer sayısı
  - `/health` 200
  - `/lib/pdfjs/pdf.min.mjs` 200
- [ ] Kullanıcı adımı:
  - Kasa/Banka detayında bir günün sırasını sürükleyip yenile
  - bir PDF giderinin küçük resmini gör
  - MCP ile 51.600 TL ATM yatırımını işle
