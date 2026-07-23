# Dashboard Dönem Tahsilat Tutarı Tasarımı

## Amaç

Dashboard’daki “Tahsilat Oranı” kartında, seçili dönemin aidat tahakkuklarına uygulanmış toplam tahsilat tutarını da göstermek.

## Kapsam ve Anlam

- Gösterilecek tutar, mevcut tahsilat oranının payı olan `collectedInPeriod` değeridir.
- Hesaplama `seçili dönem toplam tahakkuku - seçili dönem kalan borcu` şeklinde kalır.
- Tahsilat işleminin tarihi dikkate alınmaz; hangi tarihte yapılırsa yapılsın seçili dönem aidatına uygulanan tutar sayılır.
- “Tüm dönemler” seçildiğinde tüm dönemlerin tahakkuklarına uygulanmış toplam tutar gösterilir.
- “Bu Ay Gelen Aidatlar” kartının tarih bazlı hesabı değişmez.

## Veri Akışı

1. `HomeController`, hâlihazırda tahsilat oranı için hesapladığı `collectedInPeriod` değerini kullanır.
2. `DashboardViewModel` bu değeri ayrı bir alanla görünüme taşır.
3. `Views/Home/Index.cshtml`, oran kartında tutarı Türkçe para biçimiyle gösterir.

Oran ve tutar aynı hesaplama kaynağını kullandığı için yuvarlama veya kapsam farkı oluşmaz.

## Görünüm

Kartın bilgi sırası:

1. “Tahsilat Oranı” etiketi
2. Ana değer olarak mevcut yüzde
3. `Toplanan: 8.181,00 TL` biçiminde ikincil tutar satırı
4. Mevcut “Tahsil edilen / toplam tahakkuk” açıklaması

Kartın boyutu ve diğer dashboard kartlarının yerleşimi değişmez.

## Testler

- Controller/model testi, seçili dönem için hesaplanan tahsilat tutarının view modele aktarıldığını doğrular.
- Razor görünüm testi, “Toplanan” etiketinin ve model alanının kartta kullanıldığını doğrular.
- Mevcut dashboard ve tüm proje testleri regresyon kontrolü olarak çalıştırılır.

## Kapsam Dışı

- Tahsilat oranı formülünü değiştirmek
- Tahsilatları işlem tarihine göre dönemlemek
- “Bu Ay Gelen Aidatlar” kartını değiştirmek
- Yeni veritabanı alanı veya migration eklemek
