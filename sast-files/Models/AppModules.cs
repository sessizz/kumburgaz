namespace Kumburgaz.Web.Models;

// Rol yetki matrisi ile yönetilen işlevsel modüller.
// Her modül bir veya birkaç controller'ı kapsar; controller'lar [ModuleAuthorize(<Key>)] ile bu modüle bağlanır.
// Sistem yönetimi controller'ları (Settings, SystemUsers, Audit, Backups) matris dışıdır ve
// yalnızca SistemYonetici'ye açık kalır (AppPolicies.SystemAdmin).
public sealed record AppModule(string Key, string DisplayName, string Icon);

public static class AppModules
{
    public const string Panel = "Panel";
    public const string Daireler = "Daireler";
    public const string Hesaplar = "Hesaplar";
    public const string Aidatlar = "Aidatlar";
    public const string Tahsilatlar = "Tahsilatlar";
    public const string KasaBanka = "KasaBanka";
    public const string Muhasebe = "Muhasebe";
    public const string Duyurular = "Duyurular";
    public const string Talepler = "Talepler";
    public const string Belgeler = "Belgeler";
    public const string Raporlar = "Raporlar";

    // Matriste görünen modüller (sıralı). Yetki ekranı ve tohum bu listeyi kullanır.
    public static readonly IReadOnlyList<AppModule> All =
    [
        new(Panel, "Genel Bakış", "dashboard"),
        new(Daireler, "Daireler & Bloklar", "apartment"),
        new(Hesaplar, "Hesaplar", "contacts"),
        new(Aidatlar, "Aidatlar", "receipt_long"),
        new(Tahsilatlar, "Tahsilatlar", "payments"),
        new(KasaBanka, "Kasa & Banka", "account_balance_wallet"),
        new(Muhasebe, "Muhasebe (Gelir/Gider)", "trending_up"),
        new(Duyurular, "Duyurular", "campaign"),
        new(Talepler, "Talepler", "support_agent"),
        new(Belgeler, "Belgeler", "folder"),
        new(Raporlar, "Raporlar", "bar_chart"),
    ];

    public static string Display(string key)
        => All.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? key;
}
