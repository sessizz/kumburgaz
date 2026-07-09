# Belge Dosya Yukleme Tasarimi

## Amac

Belgeler modulundeki URL tabanli kaydi, dosya icerigini PostgreSQL'de saklayan bir arsiv akisi ile degistirmek. Kullanici belge listesinden detay sayfasina gider, dosyayi onizler veya indirir.

## Kapsam

- Yeni belge formundan URL alani kaldirilir.
- Yeni belge kaydi baslik, kategori, belge tarihi, not ve coklu dosya yukleme alanini icerir.
- Yuklenen her dosya mevcut `Attachment` tablosunda `EntityType = DocumentRecord` ve ilgili belge kimligi ile saklanir.
- Dosya icerigi, dosya adi, MIME turu ve bayt boyutu PostgreSQL'de tutulur.
- Belge listesinde kayit satirina tiklamak belge detayina gider.
- Detay ekrani belge metadatasini ve ek dosya listesini gosterir.
- Detay ekranindaki dosya icin onizleme ve indirme aksiyonlari sunulur.
- Bir belge kaydi bir veya daha fazla dosya icerebilir.
- Dosya basina ust sinir 25 MB'dir.

## Dosya Turleri ve Onizleme

Sunucu, hem dosya uzantisini hem de istemciden gelen MIME turunu izin listesine gore kontrol eder. Ilk izin listesi PDF, JPEG, PNG, WEBP, GIF, DOCX, XLSX, XLS, CSV ve TXT turlerini kapsar.

- PDF ve gorseller, izin kontrolunden gecen dosya yanitinin tarayicida satir ici acilmasiyla onizlenir.
- DOCX, belge icerigini `docxjs` ile HTML olarak cizer.
- XLSX, XLS ve CSV, SheetJS ile okunur; kullanici calisma sayfasini secer ve secilen sayfa HTML tablo olarak cizilir.
- Diger izinli turler indirme aksiyonuyla erisilebilir; onizleme kontrolu kullaniciya desteklenmedigini bildirir.

Onizleme kitapliklari sadece belge detay ekraninda yuklenir. Dosya baytlari yetkili `PreviewFile` ucundan alinir; sunucu yaniti tarayiciya istemci tarafinda islenmek uzere doner. Indirme ucu her zaman `Content-Disposition: attachment` kullanir.

## Sunucu Akisi

`DocumentsController` yeni bir multipart form verisini kabul eder. Form gecersizse veya dosya izin listesi / boyut sinirini asmissa, hata ayni ekranda gosterilir ve kayit olusmaz. Basarili kayit once `DocumentRecord` olarak yazilir, sonra dosyalar `Attachment` satirlari halinde eklenir.

Belge detay, ilgili `Attachment` kayitlarini sorgular. Dosya okuma, indirme ve onizleme aksiyonlari belge modulu yetkisini korur ve sadece `DocumentRecord` eklerini dondurur. Silme, belgeyi ve ona bagli tum ekleri siler.

Mevcut `DocumentRecord.Url` veritabani alani korunur ancak yeni veya duzenlenmis belge akisi tarafindan okunmaz ya da yazilmaz. Bu, eski URL verisini aniden yok etmeden URL girisini urunden kaldirir.

## Arayuz

- Form `multipart/form-data` kullanir; dosya secicisi `multiple` niteligini ve izinli uzantilari belirtir. Kullanici bir belge kaydina giderlerde oldugu gibi birden fazla dosya ekleyebilir.
- Belge listesi baglanti sutunu yerine dosya sayisini ve dosya durumunu gosterir.
- Belge detayinda onizleme alani ayrica render edilir; indirme sabit bir komuttur.
- Duzenleme ekrani metadatalari degistirir, ek dosya yukleme ve mevcut her eki tek tek silme aksiyonlarini sunar. Yeni dosyalar mevcut ekleri degistirmez; belgeye eklenir.

## Testler

Testler asagidaki davranislari kapsar:

- Kabul edilen PDF dosyasinin belge eki olarak PostgreSQL'ye kaydedilmesi.
- 25 MB sinirini asan dosyanin reddedilmesi.
- Izin disi uzantinin reddedilmesi.
- Belgeye ait ekin onizleme ve indirme aksiyonlari tarafindan okunabilmesi.
- Baska bir varlik turune ait ekin belge dosya ucundan erisilememesi.
- Belge silindiginde ona bagli eklerin de silinmesi.

## Sinirlar

Bu degisiklik, Word ve Excel dokumanlarinin tam duzenleme uyumlulugunu hedeflemez. DOCX ve elektronik tablo onizlemeleri tarayicidaki okunabilir sunumdur; indirme orijinal dosyayi her zaman korur.
