# Kumburgaz Gelistirme Plani

Bu dosya uygulama gelistirmelerini asamali ilerletmek icin tutulur.

Son guncelleme: 2026-07-08
Aktif branch: `codex/kumburgaz-improvement-plan`

## Genel Durum

- Asama 1: Buyuk olcude tamamlandi.
- Asama 2: Buyuk olcude tamamlandi.
- Asama 3: Tamamlandi.
- Asama 4: Raporlarin buyuk bolumu tamamlandi, dashboard/global arama/cikti tarafinda ana isler yapildi.
- Asama 5: Yedekleme ve geri yukleme tamamlandi, PostgreSQL 17 araci duzeltildi.
- Asama 6: Test projesi eklendi ve temel senaryolar yazildi; test kapsami genisletilmeye devam etmeli.

## Asama 1: Guvenlik, Roller, Audit ve Soft Delete

Durum: Tamamlandi.

Tamamlananlar:

- Herkese acik kayit kapatildi.
- Roller netlestirildi: `SistemYonetici`, `SiteYonetici`, `MuhasebeGorevli`, `Personel`, `SadeceGoruntuleme`.
- Controller/action bazli yetki politikalari eklendi.
- `AuditLog` tablosu ve denetim ekrani eklendi.
- Create/update/delete/restore/import/rollback islemleri audit'e yazilmaya baslandi.
- Finans ve tanim kayitlarinda soft delete altyapisi eklendi.
- Audit ekraninda silinen kayit icin geri alma islemi eklendi.
- Audit detaylari genisletildi; eski/yeni degerler ve silinen kayit bilgileri daha okunur hale getirildi.
- Geri alma oncesi kullaniciya neyi geri aldigini anlatan onay akisi eklendi.
- Sahte dashboard verileri canli ekrandan temizlendi.

Kalan / kontrol edilecekler:

- Soft delete kapsami tum eski controller delete aksiyonlarinda tekrar gozden gecirilmeli.
- Runtime'da calisan tek seferlik veri duzeltmeleri tamamen migration/admin bakim komutuna tasindi mi son kez kontrol edilmeli.
- Audit ekraninda filtreleme ve detay arama eklenebilir.

## Asama 2: Import Guvenligi

Durum: Buyuk olcude tamamlandi.

Tamamlananlar:

- `ImportBatch` ve `ImportBatchRow` tablolari eklendi.
- Import onizleme satirlari `Hazir`, `Mukerrer`, `Hatali`, `Atlandi` durumlariyla gosteriliyor.
- Import commit islemleri batch mantigiyla kayda geciyor.
- Commit edilmis import batch'i geri alma altyapisi eklendi.
- Hatali satirlar ekrandan gorulebilir ve CSV olarak indirilebilir hale getirildi.
- Kasa/banka import ekraninda gelir, gider, tahsilat ve transfer tipleri desteklendi.
- Para transferleri gelir/gider olarak raporlanmayacak sekilde ayrildi.
- Import ekraninda gelir tipi ve gelir kategorisi destegi eklendi.
- Import onizleme ekraninda yatay kayma azaltildi; gereksiz alanlar sadelestirildi, makbuz no alani korundu.

Kalan / kontrol edilecekler:

- Import rollback sonrasi olusan tum bagli kayitlarin soft delete durumlari daha fazla senaryoda test edilmeli.
- Mukerrer yakalama kurallari canli CSV ornekleriyle genisletilebilir.
- Import batch detay ekraninda satir bazli olusan kayit linkleri daha belirgin hale getirilebilir.

## Asama 3: Tek Hesap Defteri ve Finansal Tutarlilik

Durum: Buyuk olcude tamamlandi.

Tamamlananlar:

- Daire bazli merkezi `UnitLedgerService` olusturuldu.
- Daire detayi, malik detayi, ekstre, borc/alacak raporu ve dashboard ayni bakiye mantigina yaklastirildi.
- Pozitif bakiye borc, negatif bakiye alacak/avans olarak ele aliniyor.
- Devir alacagi aidattan dusuyor.
- Devir borcu net borca ekleniyor.
- Tahsilat en eski acik aidattan kapatiliyor.
- Fazla tahsilat avans/alacak olarak gorunuyor.
- Yeni aidat ve mevcut avans senaryolari icin temel hesaplama duzeltmeleri yapildi.
- Tahsilat allocation sorunlarini yakalayan tutarlilik kontrolleri eklendi.
- Tutarlilik uyarilari denetim ekraninda daha detayli gosteriliyor.
- Nuri Huri ve benzeri odeme yaptigi halde borclu gorunme senaryolari icin allocation/ledger kontrolu genisletildi.
- Denetim ekranina tek tahsilat tahsisati onarma aksiyonu eklendi.
- Denetim ekranina tum tahsilat tahsisatlarini yeniden hesaplayan admin araci eklendi.
- Onarim sonrasi tutarlilik kontrolu otomatik tekrar calisiyor ve sonuc bildiriliyor.
- Daire detayinda net bakiye, toplam tahakkuk, toplam tahsilat, devir borcu, devir alacagi ve avans daha acik gosteriliyor.
- Malik detayinda hesap ozeti kutulari genisletildi.
- Tahsilat ekraninda en eski borctan kapatma ve avans kalma mantigi acik metinle gosteriliyor.
- Tahsilat allocation onarimi icin otomatik test eklendi.
- Tahsilat tutari degistikce borca uygulanacak ve avans kalacak tutarlar canli guncelleniyor.
- Tahsilat edit/delete allocation geri alma davranisi otomatik testlerle guvenceye alindi.

Kalan / kontrol edilecekler:

- Bu asama icin zorunlu kalan is yok. Ilave link/UX iyilestirmeleri Asama 4 veya Asama 6 kapsaminda ele alinabilir.

## Asama 4: Gelir Raporlari, Dashboard, Global Arama ve Ciktilar

Durum: Buyuk bolumu tamamlandi.

Tamamlananlar:

- Gelir ekrani gider ekraniyla ayni seviyeye yaklastirildi.
- Gider ekranina kategori ve tarih araligi filtresi eklendi.
- Gider icmalinde kasa ve banka rakamlari ayrildi.
- Gelir Raporu eklendi.
- Gelir/Gider Ozeti eklendi.
- Aylik Nakit Akisi eklendi.
- Borc/Alacak Yaslandirma raporu eklendi.
- Daire Borc/Alacak raporunda donem filtresi kaldirildi; her dairenin toplam borc/alacak bakiyesi gosteriliyor.
- Daire Borc/Alacak raporunda daire no ve sorumlu hesap linkleri ilgili detay sayfalarina gidiyor.
- Excel/PDF ciktilarina aktif filtre ozeti eklenmeye baslandi.
- Global arama daire, malik, makbuz no, aciklama, banka/kasa hareketi, kategori, belge ve talep arayacak sekilde genisletildi.
- Dashboard gercek uyari kartlariyla yenilendi:
  - Vadesi gecmis borc
  - Yuksek alacak/avans
  - Tutarlilik kontrolu
  - Problemli import satirlari
  - Bu ay nakit eksi durumu
  - Yedekleme uyarisi
- Hazirun cetveli eklendi; Excel/PDF cikti destekli.
- Hazirun cetvelinden telefon kaldirildi.
- Hazirun cetveli kullanici ornek dosyasindaki duzene yaklastirildi.
- Aidat durum cetveli eklendi; Excel/PDF cikti destekli.
- Aidat durum cetveli kullanici ornek dosyasindaki yatay, kompakt duzene yaklastirildi.
- Birlesik daire adlari raporlarda sade gosterilecek sekilde duzeltildi.
- Hazirun ve aidat durum Excel/PDF ciktilarina filtre ozeti eklendi.

Kalan / kontrol edilecekler:

- Tum Excel/PDF ciktilarinda filtre ozeti standardi eski raporlar icin son kez gozden gecirilmeli.
- Gelir raporlarinda kategori, kasa/banka ve tarih bazli ozet kartlari daha da iyilestirilebilir.
- Dashboard kartlari icin esik degerler ayarlanabilir yapilabilir.
- Global arama sonuc ekraninda daha iyi gruplama ve klavye ile secim eklenebilir.
- Hazirun ve aidat durum cetveli canli yazici/PDF ciktilariyla son kez karsilastirilmali.

## Asama 5: Yedekleme, Geri Yukleme ve Operasyon

Durum: Tamamlandi, operasyonel iyilestirme yapilabilir.

Tamamlananlar:

- Sistem yoneticisine ozel yedekleme ekrani eklendi.
- Gunluk otomatik yedek destegi eklendi.
- Manuel yedek alma ve indirme eklendi.
- Geri yukleme destegi eklendi.
- Geri yukleme oncesi otomatik "restore oncesi" yedek aliniyor.
- PostgreSQL icin `pg_dump/pg_restore`, SQLite icin dosya kopyalama destekleniyor.
- Coolify/container ortaminda `pg_dump` bulunamama sorunu icin arac kurulumu/ayar destegi eklendi.
- PostgreSQL 17 server ile pg_dump 16 uyumsuzlugu giderildi.
- Yedekleme ve geri yukleme islemleri audit'e yaziliyor.
- Dashboard son yedek durumunu ve gecikmis yedek uyarisini gosteriyor.

Kalan / kontrol edilecekler:

- Geri yukleme islemi canli ortamda dikkatli bir test proseduruyle belgelenmeli.
- Yedek dosyalarinin saklama suresi ve disk doluluk uyarisi dashboard'a eklenebilir.

## Asama 6: Otomatik Testler

Durum: Basladi ve temel kapsam var.

Tamamlananlar:

- `Kumburgaz.Web.Tests` test projesi eklendi.
- Devir alacagi aidattan duser senaryosu test edildi.
- Devir borcu net borca eklenir senaryosu test edildi.
- Tahsilat en eski borctan kapatir senaryosu test edildi.
- Fazla tahsilat avans olur senaryosu test edildi.
- Yeni aidat olusunca avans dusme mantigi test edildi.
- Import mukerrer satir yakalama senaryolari test edildi.
- Import rollback davranisi icin temel testler eklendi.
- Gelir/gider/transfer kasa-banka bakiyesini etkiler senaryolari test edilmeye baslandi.
- Rapor/ekstre/daire detay bakiyesi icin temel tutarlilik testleri eklendi.
- Rapor export formatlari icin temel test eklendi.
- Son test sonucu: `dotnet test .\kumburgaz.sln` basarili, 13 test gecti.

Kalan / kontrol edilecekler:

- Yetkisiz kullanici finans/admin islemlerine erisemez testleri genisletilmeli.
- Tahsilat edit/delete allocation geri alma testleri eklendi; yeni senaryolar geldikce artirilabilir.
- Audit restore ve soft delete davranislari icin testler eklenmeli.
- Backup/restore servisleri icin mock veya sqlite tabanli testler eklenmeli.
- Dashboard uyari kartlari icin controller/view model testleri eklenebilir.

## Siradaki Onerilen Isler

1. Asama 6 test genisletme:
   - Tahsilat edit/delete, audit restore ve yetki testlerini ekle.

2. Asama 4 cikti standardi:
   - Tum Excel/PDF raporlarinda filtre ozeti ve tarih bilgisi ayni formatta gosterilsin.

3. Asama 5 operasyon:
   - Yedek saklama suresi, disk doluluk ve son yedek gecikme esikleri ayarlanabilir olsun.

## Kabul Kriterleri Durumu

- Ayni daire icin daire detayi, malik detayi, ekstre, borc/alacak raporu ve dashboard ayni net bakiyeyi gostermeli: Buyuk olcude tamamlandi; yeni veri onarim araci eski allocation sorunlarini duzeltmek icin eklendi.
- Import edilen her dosyanin batch numarasi olmali: Tamamlandi.
- Hangi satirdan hangi kaydin olustugu gorulebilmeli: Kismi tamamlandi, linkler iyilestirilebilir.
- Mukerrer import kayit uretmemeli: Tamamlandi, daha fazla canli CSV testi onerilir.
- Finansal kayit silinince fiziksel olarak kaybolmamali: Buyuk olcude tamamlandi.
- Audit ve geri alma mumkun olmali: Tamamlandi, detay gorunumu iyilestirildi.
- Gelirler ayri raporlanabilmeli: Tamamlandi.
- Gelir/gider ozeti alinabilmeli: Tamamlandi.
- Yonetici son yedek zamanini gorebilmeli: Tamamlandi.
- Manuel yedek indirilebilmeli ve geri yukleme yapilabilmeli: Tamamlandi.
- Register herkese acik olmamali: Tamamlandi.
- Test projesi temel finansal senaryolari otomatik dogrulamali: Basladi, 15 test mevcut.
