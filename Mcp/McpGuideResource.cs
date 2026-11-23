using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Kumburgaz.Web.Mcp;

// Claude'un kavramları okuduğu rehber; araç açıklamalarına sığmayan site kuralları burada.
[McpServerResourceType]
public class McpGuideResource
{
    private static readonly string Text =
        """
        # Kumburgaz rehberi

        ## Aidat grubu
        Aynı aidat tipini (tutarını) ödeyen dairelerin grubu. Dönem aidatı, gruptaki her aktif daireye ayrı taksit olarak yazılır. Birleştirilmiş gruplarda ise grubun tamamı için tek taksit oluşur. Tahsilat girilebilmesi için dairenin aktif bir aidat grubu olmalıdır.

        ## Birleşik daire
        Aynı bloktaki iki fiziksel dairenin tek daire olarak yönetilmesi (örnek: A-5+6). Aidat ve tahsilat birleşik daireye işlenir; onu oluşturan fiziksel daireler pasif olur.

        ## Dönem
        Aidat dönemi "YYYY-YYYY" biçimindedir ve 1 Temmuz'da başlar. "2026-2027", 01.07.2026 ile 30.06.2027 arasıdır.

        ## Devir bakiyesi
        Önceki yönetimden devreden bakiye. Borç ise devir borcudur: dönem taksitlerinden ayrı izlenir. Alacak ise dairenin aidatlarından düşülür. Tahsilatta dönem yerine "devir" verilirse önce devir borcu kapatılır, kalan tutar açık taksitlere eskiden yeniye dağıtılır.

        ## Avans
        Tahsilatın hiçbir borca dağıtılmayan kısmı. Belirli bir döneme yapılan tahsilat o taksiti aşarsa fazlası avans kalır. Avans, yeni aidat üretildiğinde dairenin açık borçlarına otomatik sayılır.

        ## Mahsup
        Daire adına ödenen site giderinin aidat borcundan düşülmesi. Ana kasaya hem tahsilat hem gider yazılır, kasa bakiyesi değişmez. İki kayıt birbirine bağlıdır ve yalnız birlikte silinir. Bu bağlantıyla mahsup girilemez, uygulamadan yapılır.

        ## Kasa/banka anahtarı
        Hesaplar anahtarla belirtilir: kasa için `cash:{id}`, banka hesabı için `bank:{id}` (örnek: `cash:1`, `bank:2`). Geçerli anahtarlar cash_bank_accounts aracından alınır.

        ## Transfer
        Kasa ile banka (ya da iki hesap) arasındaki para aktarımı gelir ya da gider değildir. Kasadaki nakdin ATM'den bankaya yatırılması record_transfer ile kaydedilir (kaynak: kasa, hedef: banka); create_income/create_expense kullanılmaz. Kaynak hesaptan çıkış, hedef hesaba giriş olmak üzere iki satır yazılır; açıklama kasadan bankaya "Bankaya yatır", diğer yönlerde "Para transferi" ile başlar.

        ## Tarih ve tutar
        Araçlara tarih YYYY-AA-GG biçiminde verilir; verilmezse Türkiye saatine göre bugün kullanılır. Tutar sayı ya da "2.500,00" gibi Türkçe metin olabilir ve sıfırdan büyük olmalıdır.
        """.ReplaceLineEndings("\n");

    [McpServerResource(UriTemplate = "kumburgaz://rehber", Name = "rehber", Title = "Kumburgaz rehberi", MimeType = "text/markdown")]
    [Description("Aidat grubu, birleşik daire, dönem, devir bakiyesi, avans, mahsup ve kasa/banka anahtarlarının açıklaması.")]
    public static string Read() => Text;
}
