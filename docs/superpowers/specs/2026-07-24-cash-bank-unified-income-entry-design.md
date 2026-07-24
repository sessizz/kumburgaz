# Kasa/Banka Birleşik Gelir Girişi Tasarımı

## Amaç

Kasa/Banka hesap detayındaki “Tahsilat” girişini “Gelir” olarak genelleştirmek. Kullanıcı aynı modal üzerinden aidat tahsilatı veya faiz geliri gibi normal gelir kaydı oluşturabilmeli.

## Kullanıcı Arayüzü

- Sağ paneldeki hızlı “Tahsilat” butonu “Gelir” olarak değiştirilecek.
- “Para Hareketi” açılır menüsündeki “Tahsilat” seçeneği “Gelir” olarak değiştirilecek.
- Her iki giriş aynı gelir modalını açacak.
- Modalın ilk alanı “Gelir kategorisi” olacak.
- Seçeneklerin başında sistemsel ve kategori adından bağımsız bir “Aidat” seçeneği bulunacak.
- “Aidat” seçildiğinde mevcut dönem, aidat/daire, tarih, tutar, referans, makbuz ve not alanları gösterilecek.
- Normal bir gelir kategorisi seçildiğinde aidat alanları gizlenecek; tarih, tutar ve açıklama alanları gösterilecek.

## Veri Akışı

Birleşik form, seçimin türünü özel bir değerle taşıyacak:

- `dues`: Mevcut `CollectionService` kullanılarak gerçek aidat tahsilatı oluşturulur. Böylece tahsisat, kalan borç ve makbuz davranışları korunur.
- `category:<id>`: Seçilen aktif `Gelir` kategorisi doğrulanır ve mevcut hesap için pozitif bir `LedgerTransaction` oluşturulur.

Aidat davranışı kategori adına göre belirlenmeyecek. Böylece “Aidat Tahsilatı” kategorisinin adı değişse veya benzer isimli başka kategori eklense bile yanlış yönlendirme oluşmaz.

## Doğrulama ve Hata Davranışı

- Hesap türü ve hesap kimliği mevcut yönlendirme kurallarıyla doğrulanır.
- Tutar kültüre duyarlı mevcut tutar ayrıştırıcısıyla okunur.
- `dues` seçiminde aidat/taksit zorunludur.
- Normal gelir seçiminde kategori aktif ve `Gelir` tipinde olmalıdır.
- Geçersiz veya eksik formda kayıt oluşturulmaz; kullanıcı hesap detayına açıklayıcı hata mesajıyla döner.
- Makbuz yönlendirmesi yalnızca aidat tahsilatında çalışır.

## Testler

- Birleşik gelir formunun “Aidat” seçimiyle `Collection` oluşturduğu doğrulanır.
- Normal gelir kategorisinin pozitif `LedgerTransaction` oluşturduğu doğrulanır.
- Gider kategorisinin gelir olarak kullanılamadığı doğrulanır.
- Görünümde “Gelir” butonu, özel “Aidat” seçeneği ve koşullu aidat alanları doğrulanır.
- Mevcut Kasa/Banka ve tüm proje testleri çalıştırılır.

## Kapsam Dışı

- Gelir/Gider kategori şemasına yeni kolon eklemek.
- Mevcut kayıtların dönüştürülmesi.
- CSV import akışının değiştirilmesi.
- Tahsilat veya gelir raporlarının yeniden tasarlanması.
