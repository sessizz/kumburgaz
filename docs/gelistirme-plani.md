# Kumburgaz Gelistirme Plani

Bu dosya uygulama gelistirmelerini asamali ilerletmek icin tutulur.

## Asama 1: Guvenlik, Roller, Audit ve Soft Delete

- Herkese acik kayit kapatilacak.
- Roller netlestirilecek: SistemYonetici, SiteYonetici, MuhasebeGorevli, Personel, SadeceGoruntuleme.
- Controller/action bazli yetki politikalarina gecilecek.
- AuditLog tablosu ile create/update/delete/restore/import/rollback kayitlari tutulacak.
- Finans ve tanim kayitlarinda soft delete uygulanacak.
- Tek seferlik runtime veri duzeltmeleri migration/admin bakim isine tasinacak.
- Sahte dashboard verileri canli ekrandan kaldirilacak.

## Asama 2: Import Guvenligi

- ImportBatch ve ImportBatchRow tablolari eklenecek.
- Import onizleme satirlari Hazir, Mukerrer, Hatali, Atlandi durumlariyla gosterilecek.
- Import commit islemi tek transaction icinde yapilacak.
- Commit edilmis import batch'i geri alinabilecek.
- Hatali satirlar ekrandan ve CSV olarak indirilebilir olacak.
- Kasa/banka import ekraninda gelir, gider, tahsilat ve transfer tam desteklenecek.

## Asama 3: Tek Hesap Defteri ve Finansal Tutarlilik

- Daire bazli merkezi UnitLedgerService olusturulacak.
- Daire detayi, malik detayi, ekstre, raporlar ve dashboard ayni bakiye servisinden beslenecek.
- Pozitif bakiye borc, negatif bakiye alacak/avans olarak ele alinacak.
- Devir alacagi ve fazla tahsilat en eski aidatlardan otomatik dusulecek.
- Gecelik tutarlilik kontrol servisi eklenecek.

## Asama 4: Gelir Raporlari, Dashboard, Global Arama ve Ciktilar

- Gelir ekrani gider ekraniyla ayni seviyeye getirilecek.
- Gelir Raporu, Gelir/Gider Ozeti, Aylik Nakit Akisi ve Borc/Alacak Yaslandirma raporlari eklenecek.
- Excel/PDF ciktilarinda aktif filtre ozeti yer alacak.
- Global arama daire, malik, makbuz no, aciklama, banka/kasa hareketi, kategori, belge ve talep arayacak.
- Dashboard gercek uyari kartlariyla yenilenecek.

## Asama 5: Yedekleme, Geri Yukleme ve Operasyon

- Sistem yoneticisine ozel yedekleme ekrani eklenecek.
- Gunluk otomatik yedek, manuel yedek indirme ve geri yukleme desteklenecek.
- PostgreSQL icin pg_dump/pg_restore, SQLite icin dosya kopyalama kullanilacak.
- Yedekleme ve geri yukleme islemleri audit'e yazilacak.

## Asama 6: Otomatik Testler

- Kumburgaz.Web.Tests test projesi eklenecek.
- Devir, avans, tahsilat, import, rapor ve yetki senaryolari test edilecek.
- Her asama sonunda dotnet build ve testler calistirilacak.
