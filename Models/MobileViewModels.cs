using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Kumburgaz.Web.Models;

// Mobil gider listesi satiri.
public class MobileGiderListItem
{
    public int Id { get; set; } // LedgerTransaction.Id
    public DateTime Date { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public bool HasAttachment { get; set; }
    public int? AttachmentId { get; set; }
    public bool IsMahsup { get; set; }
    public int? MahsupId { get; set; }
    public string? UnitDisplay { get; set; }
}

// Mobil gider ekleme/duzenleme formu (yonetici normal gider + Sakin mahsup akisi).
public class MobileGiderFormViewModel
{
    public int? Id { get; set; }  // LedgerTransaction.Id; duzenlemede dolu
    public bool IsEdit { get; set; }
    public bool IsResident { get; set; }
    public int? UnitId { get; set; }
    public int? CategoryId { get; set; }
    public decimal? Amount { get; set; }

    [MaxLength(250)]
    public string? Description { get; set; }

    // Yonetici icin: normal gider mi, mahsuplu gider mi. Sakin icin her zaman true (UI'da gizli).
    public bool IsMahsup { get; set; }

    // Mahsup duzenlemesinde tutar/kategori sabittir (tahsilat/tahakkuk tutarliligini bozmamak icin);
    // yalnizca aciklama ve fotograflar degistirilebilir.
    public bool AmountCategoryEditable { get; set; } = true;

    // Mevcut fotograflari kaldirma yalnizca personel/yonetici icin acik.
    public bool CanRemoveAttachments { get; set; }

    public string? UnitDisplay { get; set; }  // duzenlemede mahsubun daire adi (salt okunur gosterim)

    public List<SelectListItem> UnitOptions { get; set; } = [];
    public List<SelectListItem> CategoryOptions { get; set; } = [];
    public List<MobileAttachmentSummary> ExistingAttachments { get; set; } = [];
}

public class MobileAttachmentSummary
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
}

// Mobil gider detay ekrani.
public class MobileGiderDetailViewModel
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string? AccountLabel { get; set; }
    public bool IsMahsup { get; set; }
    public int? MahsupId { get; set; }
    public string? UnitDisplay { get; set; }
    public List<MobileAttachmentSummary> Attachments { get; set; } = [];
    public bool CanEdit { get; set; }
    public bool CanDeleteMahsup { get; set; }
}

// Mobil yeni talep formu.
public class MobileTalepFormViewModel
{
    [Required(ErrorMessage = "Başlık zorunludur.")]
    [MaxLength(140)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public int? UnitId { get; set; }

    // Yalnızca yetkili (Sakin olmayan) kullanıcıya gösterilir; Sakinde her zaman görünür işaretlenir.
    public bool IsVisibleToResidents { get; set; } = true;

    public List<SelectListItem> UnitOptions { get; set; } = [];
    public bool IsResident { get; set; }
}

// Sakinin kendi PIN'ini degistirmesi.
public class MobileChangePasswordViewModel
{
    [Required(ErrorMessage = "Mevcut PIN zorunludur.")]
    public string CurrentPin { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni PIN zorunludur.")]
    [RegularExpression(@"^\d{5}$", ErrorMessage = "Yeni PIN 5 haneli sayı olmalıdır.")]
    public string NewPin { get; set; } = string.Empty;

    [Required(ErrorMessage = "PIN tekrarı zorunludur.")]
    public string ConfirmPin { get; set; } = string.Empty;
}

// Hesap duzenleme sayfasi: form + daire erisimi + mobil giris yonetimi.
public class AccountUnitAccessRow
{
    public int UnitId { get; set; }
    public string Display { get; set; } = string.Empty;
    public string? Role { get; set; }   // sahiplik satirinda malik/kiraci
    public bool IsOwned { get; set; }    // true = sahiplik (salt okunur), false = ek erisim (kaldirilabilir)
}

public class AccountEditPageViewModel
{
    public AccountFormViewModel Form { get; set; } = new();
    public bool IsResidentAccount { get; set; }
    public bool CanManageLogin { get; set; }   // SistemYonetici
    public bool HasLogin { get; set; }
    public string UserName { get; set; } = string.Empty;   // = hesap Id
    public string? MobilePassword { get; set; }
    public List<AccountUnitAccessRow> OwnedUnits { get; set; } = [];
    public List<AccountUnitAccessRow> GrantedUnits { get; set; } = [];
    public List<SelectListItem> UnitOptions { get; set; } = [];
}

// Kullanici Giris Bilgileri raporu satiri (SistemYonetici gorur).
public class ResidentCredentialRow
{
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string AccountTypeLabel { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string UserName { get; set; } = string.Empty;   // = AccountId
    public string? Password { get; set; }                  // 5 haneli PIN
    public string Units { get; set; } = string.Empty;      // erisimindeki daireler (virgullu)
    public bool HasLogin { get; set; }
}

// Mobil (PWA) ekranlari icin view modelleri. Asama 1: Panel + Daireler.

public class MobileCategoryAmount
{
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class MobilePanelViewModel
{
    public decimal MonthCollections { get; set; }
    public decimal TotalCollections { get; set; }
    public int DebtorCount { get; set; }
    public decimal TotalDebt { get; set; }
    public decimal TotalCredit { get; set; }

    public string MonthLabel { get; set; } = string.Empty;
    public string LastMonthLabel { get; set; } = string.Empty;
    public decimal MonthExpenseTotal { get; set; }
    public decimal LastMonthExpenseTotal { get; set; }
    public List<MobileCategoryAmount> MonthExpenses { get; set; } = [];
    public List<MobileCategoryAmount> LastMonthExpenses { get; set; } = [];
}

public class MobileUnitListItem
{
    public int Id { get; set; }
    public string UnitNo { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
    public string? OwnerName { get; set; }
    public string? ResponsibleAccountName { get; set; }
    // Pozitif = borc, negatif = alacak/avans, 0 = temiz
    public decimal Balance { get; set; }
}

public class MobileUnitBlockGroup
{
    public string BlockName { get; set; } = string.Empty;
    public List<MobileUnitListItem> Units { get; set; } = [];
}

public class MobileUnitListViewModel
{
    public string? Query { get; set; }
    public string Status { get; set; } = "all";
    public int TotalCount { get; set; }
    public int DebtorCount { get; set; }
    public int CreditorCount { get; set; }
    public int CleanCount { get; set; }
    public List<MobileUnitBlockGroup> Blocks { get; set; } = [];
}

// Sakin ana ekrani.
public class MobileResidentPaymentRow
{
    public DateTime Date { get; set; }
    public string UnitDisplay { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class MobileResidentPanelViewModel
{
    public bool HasUnits { get; set; }
    public decimal Balance { get; set; }       // pozitif = borc, negatif = alacak
    public int UnitCount { get; set; }
    public List<MobileUnitListItem> Units { get; set; } = [];
    public List<MobileResidentPaymentRow> RecentPayments { get; set; } = [];
    public List<Announcement> RecentAnnouncements { get; set; } = [];
}

public class MobileUnitDetailViewModel
{
    public Unit Unit { get; set; } = null!;
    public decimal Balance { get; set; }
    public UnitLedgerSummary Summary { get; set; } = new();
    // Tahakkuklar: devir bakiyesi + aidat borclari (tarih azalan)
    public List<StatementEntry> Accruals { get; set; } = [];
    // Odemeler (tarih azalan)
    public List<StatementEntry> Collections { get; set; } = [];
}
