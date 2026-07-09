using System.ComponentModel.DataAnnotations;

namespace Kumburgaz.Web.Models;

public static class AppRoles
{
    public const string SistemYonetici = "SistemYonetici";
    public const string SiteYonetici = "SiteYonetici";
    public const string MuhasebeGorevli = "MuhasebeGorevli";
    public const string Personel = "Personel";
    public const string SadeceGoruntuleme = "SadeceGoruntuleme";
    // Daire sahibi/kiracı için mobil (Sakin) rolü. Yalnızca mobil alanda çalışır;
    // masaüstü ekranları SakinAreaRestrictionFilter ile kapalıdır.
    public const string Sakin = "Sakin";

    public static readonly string[] All =
    [
        SistemYonetici,
        SiteYonetici,
        MuhasebeGorevli,
        Personel,
        SadeceGoruntuleme,
        Sakin
    ];

    // Rol anahtarları ile kullanıcıya gösterilen Türkçe adları eşleyen tek kaynak.
    // Hem Ayarlar > Kullanıcılar hem de Hesabım/Profil bu adları kullanır (tutarlılık).
    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [SistemYonetici] = "Sistem Yöneticisi",
        [SiteYonetici] = "Site Yöneticisi",
        [MuhasebeGorevli] = "Muhasebe Görevlisi",
        [Personel] = "Personel",
        [SadeceGoruntuleme] = "Sadece Görüntüleme",
        [Sakin] = "Sakin (Daire Sahibi/Kiracı)"
    };

    public static string Display(string? roleName)
        => roleName is not null && DisplayNames.TryGetValue(roleName, out var name) ? name : roleName ?? string.Empty;
}

public static class AppPolicies
{
    public const string SystemAdmin = "SystemAdmin";
    public const string FinanceWrite = "FinanceWrite";
    public const string ManagementWrite = "ManagementWrite";
    public const string ReportsRead = "ReportsRead";
}

// Bir rolün bir modüldeki erişim düzeyi. Sistem yöneticisi bu matrisi düzenler.
// CanWrite doğruysa görüntüleme de kapsanır (yazma, görmeyi ima eder).
public class RolePermission
{
    public int Id { get; set; }

    [Required, MaxLength(64)]
    public string RoleName { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string Module { get; set; } = string.Empty;

    public bool CanView { get; set; }
    public bool CanWrite { get; set; }
}

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    string? DeletedByUserId { get; set; }
    string? DeletedByUserName { get; set; }
}

public enum AuditAction
{
    Create = 1,
    Update = 2,
    Delete = 3,
    Restore = 4,
    Import = 5,
    Rollback = 6,
    LoginSensitiveAction = 7
}

public class AuditLog
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string EntityName { get; set; } = string.Empty;

    [MaxLength(80)]
    public string EntityId { get; set; } = string.Empty;

    public AuditAction Action { get; set; }

    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }

    [MaxLength(450)]
    public string? UserId { get; set; }

    [MaxLength(256)]
    public string? UserName { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(80)]
    public string? CorrelationId { get; set; }
}

public enum ImportBatchStatus
{
    Draft = 1,
    Committed = 2,
    RolledBack = 3,
    Failed = 4
}

public enum ImportRowStatus
{
    Ready = 1,
    Duplicate = 2,
    Error = 3,
    Skipped = 4,
    Committed = 5,
    RolledBack = 6
}

public class ImportBatch
{
    public int Id { get; set; }

    [Required, MaxLength(40)]
    public string ImportNo { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string Type { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? SourceAccountKind { get; set; }

    public int? SourceAccountId { get; set; }

    [MaxLength(260)]
    public string? FileName { get; set; }

    [MaxLength(128)]
    public string? FileHash { get; set; }

    public ImportBatchStatus Status { get; set; } = ImportBatchStatus.Draft;

    [MaxLength(256)]
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CommittedAt { get; set; }
    public DateTime? RolledBackAt { get; set; }

    public ICollection<ImportBatchRow> Rows { get; set; } = new List<ImportBatchRow>();
}

public class ImportBatchRow
{
    public int Id { get; set; }
    public int ImportBatchId { get; set; }
    public ImportBatch? ImportBatch { get; set; }
    public int LineNo { get; set; }
    public string RawJson { get; set; } = "{}";

    [Required, MaxLength(300)]
    public string NormalizedKey { get; set; } = string.Empty;

    public ImportRowStatus Status { get; set; } = ImportRowStatus.Ready;

    [MaxLength(500)]
    public string? ErrorMessage { get; set; }

    [MaxLength(80)]
    public string? CreatedEntityName { get; set; }

    public int? CreatedEntityId { get; set; }
}

public class ConsistencyCheckResult
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string CheckName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Severity { get; set; } = "Info";

    [Required, MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? EntityName { get; set; }

    [MaxLength(80)]
    public string? EntityId { get; set; }

    public decimal? Difference { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool Resolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public enum PaymentChannel
{
    [Display(Name = "Nakit")]
    Cash = 1,
    [Display(Name = "Banka")]
    Bank = 2
}

public enum InstallmentStatus
{
    [Display(Name = "Açık")]
    Open = 1,
    [Display(Name = "Kısmi Ödendi")]
    PartiallyPaid = 2,
    [Display(Name = "Ödendi")]
    Paid = 3
}

public enum AccountType
{
    [Display(Name = "Malik")]
    Owner = 1,
    [Display(Name = "Kiracı")]
    Tenant = 2,
    [Display(Name = "Personel")]
    Personnel = 3,
    [Display(Name = "Tedarikçi")]
    Supplier = 4
}

public enum UnitAccountRole
{
    [Display(Name = "Malik")]
    Owner = 1,
    [Display(Name = "Kiracı")]
    Tenant = 2
}

public enum DuesPayerType
{
    [Display(Name = "Malik")]
    Owner = 1,
    [Display(Name = "Kiracı varsa kiracı, yoksa malik")]
    Tenant = 2
}

public class Site
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;
}

public class Block : ISoftDeletable
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public Site? Site { get; set; }

    [Required, MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeletedByUserName { get; set; }

    public ICollection<Unit> Units { get; set; } = new List<Unit>();
}

public class Unit : ISoftDeletable
{
    public int Id { get; set; }
    public int BlockId { get; set; }
    public Block? Block { get; set; }

    [Required, MaxLength(30)]
    public string UnitNo { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? OwnerName { get; set; }

    [MaxLength(40)]
    public string? Phone { get; set; }

    /// <summary>Malikin daireye giriş tarihi.</summary>
    public DateTime? MoveInDate { get; set; }

    public bool Active { get; set; } = true;
    public bool IsCombined { get; set; }
    public DuesPayerType DuesPayerType { get; set; } = DuesPayerType.Owner;

    /// <summary>
    /// Önceki yönetimden devreden bakiye.
    /// Pozitif = daire alacaklı (gelecek aidatlardan düşülür).
    /// Negatif = daire borçlu (devreden borç).
    /// </summary>
    public decimal OpeningBalance { get; set; }

    /// <summary>
    /// Devir bakiyesinin geçerli olduğu tarih (önceki yönetim devir tarihi).
    /// Null ise bakiye atanmamış demektir; raporlarda satır gösterilmez.
    /// </summary>
    public DateTime? OpeningBalanceDate { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeletedByUserName { get; set; }

    public ICollection<BillingGroupUnit> BillingGroupUnits { get; set; } = new List<BillingGroupUnit>();
    public ICollection<UnitAccount> UnitAccounts { get; set; } = new List<UnitAccount>();
    public ICollection<CombinedUnitMember> CombinedUnitMembers { get; set; } = new List<CombinedUnitMember>();
    public ICollection<CombinedUnitMember> MemberOfCombinedUnits { get; set; } = new List<CombinedUnitMember>();
}

public class Account : ISoftDeletable
{
    public int Id { get; set; }

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    public AccountType AccountType { get; set; }

    [MaxLength(40)]
    public string? Phone { get; set; }

    [MaxLength(160), EmailAddress]
    public string? Email { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public bool Active { get; set; } = true;

    /// <summary>
    /// Mobil giriş için 5 haneli PIN (yalnızca Malik/Kiracı hesaplarında dolu).
    /// Sistem yöneticisi bu değeri "Kullanıcı Giriş Bilgileri" raporunda görebilir.
    /// Kullanıcı adı olarak hesabın Id'si kullanılır.
    /// </summary>
    [MaxLength(12)]
    public string? MobilePassword { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeletedByUserName { get; set; }

    public ICollection<UnitAccount> UnitAccounts { get; set; } = new List<UnitAccount>();
    public ICollection<DuesInstallment> DuesInstallments { get; set; } = new List<DuesInstallment>();
    public ICollection<AccountUnitAccess> UnitAccessGrants { get; set; } = new List<AccountUnitAccess>();
}

/// <summary>
/// Bir hesaba (Sakin) sahipliğinden bağımsız olarak ek daire erişimi tanımlar.
/// Örnek: Malik B8/C24'ün sahibi ama C21'de yakını oturuyor ve aidatını o ödüyor;
/// C21 bu tablo ile hesabın erişimine eklenir. Sistem/site yöneticisi düzenler.
/// </summary>
public class AccountUnitAccess
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account? Account { get; set; }
    public int UnitId { get; set; }
    public Unit? Unit { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }
    [MaxLength(256)]
    public string? CreatedByUserName { get; set; }
}

public class UnitAccount : ISoftDeletable
{
    public int Id { get; set; }
    public int UnitId { get; set; }
    public Unit? Unit { get; set; }
    public int AccountId { get; set; }
    public Account? Account { get; set; }
    public UnitAccountRole Role { get; set; }
    public bool Active { get; set; } = true;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeletedByUserName { get; set; }
}

public class CombinedUnitMember : ISoftDeletable
{
    public int Id { get; set; }
    public int CombinedUnitId { get; set; }
    public Unit? CombinedUnit { get; set; }
    public int ComponentUnitId { get; set; }
    public Unit? ComponentUnit { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeletedByUserName { get; set; }
}

public class DuesType : ISoftDeletable
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 999999999, ErrorMessage = "Tutar 0 ile 999.999.999 arasında olmalıdır.")]
    public decimal Amount { get; set; }

    public bool Active { get; set; } = true;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeletedByUserName { get; set; }

    public ICollection<BillingGroup> BillingGroups { get; set; } = new List<BillingGroup>();
}

public class BillingGroup : ISoftDeletable
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public int DuesTypeId { get; set; }
    public DuesType? DuesType { get; set; }

    [Required, MaxLength(9)]
    public string EffectiveStartPeriod { get; set; } = string.Empty; // YYYY-YYYY

    [MaxLength(9)]
    public string? EffectiveEndPeriod { get; set; } // YYYY-YYYY

    public bool Active { get; set; } = true;
    public bool IsMerged { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeletedByUserName { get; set; }

    public ICollection<BillingGroupUnit> Units { get; set; } = new List<BillingGroupUnit>();
    public ICollection<DuesInstallment> Installments { get; set; } = new List<DuesInstallment>();
}

public class BillingGroupUnit : ISoftDeletable
{
    public int Id { get; set; }
    public int BillingGroupId { get; set; }
    public BillingGroup? BillingGroup { get; set; }

    public int UnitId { get; set; }
    public Unit? Unit { get; set; }

    [Required, MaxLength(9)]
    public string StartPeriod { get; set; } = string.Empty;

    [MaxLength(9)]
    public string? EndPeriod { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeletedByUserName { get; set; }
}

public class DuesInstallment : ISoftDeletable
{
    public int Id { get; set; }
    public int BillingGroupId { get; set; }
    public BillingGroup? BillingGroup { get; set; }
    public int? UnitId { get; set; }
    public Unit? Unit { get; set; }
    public int? ResponsibleAccountId { get; set; }
    public Account? ResponsibleAccount { get; set; }

    [Required, MaxLength(9)]
    public string Period { get; set; } = string.Empty; // YYYY-YYYY

    public DateTime AccrualDate { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal RemainingAmount { get; set; }
    public InstallmentStatus Status { get; set; } = InstallmentStatus.Open;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeletedByUserName { get; set; }

    public ICollection<CollectionAllocation> Allocations { get; set; } = new List<CollectionAllocation>();
}

public class Collection : ISoftDeletable
{
    public int Id { get; set; }
    public int BillingGroupId { get; set; }
    public BillingGroup? BillingGroup { get; set; }
    public int UnitId { get; set; }
    public Unit? Unit { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public PaymentChannel PaymentChannel { get; set; }
    public int? CashBoxId { get; set; }
    public CashBox? CashBox { get; set; }
    public int? BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    [MaxLength(80)]
    public string? ReferenceNo { get; set; }

    [MaxLength(250)]
    public string? Note { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeletedByUserName { get; set; }

    public ICollection<CollectionAllocation> Allocations { get; set; } = new List<CollectionAllocation>();
}

public class CollectionAllocation : ISoftDeletable
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public Collection? Collection { get; set; }
    public int DuesInstallmentId { get; set; }
    public DuesInstallment? DuesInstallment { get; set; }
    public decimal AppliedAmount { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeletedByUserName { get; set; }
}

public class IncomeExpenseCategory : ISoftDeletable
{
    public int Id { get; set; }

    [Required, MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Type { get; set; } = "Expense";

    public bool Active { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeletedByUserName { get; set; }
}

public class LedgerTransaction : ISoftDeletable
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public int? IncomeExpenseCategoryId { get; set; }
    public IncomeExpenseCategory? IncomeExpenseCategory { get; set; }
    public bool IsTransfer { get; set; }
    public bool TransferIsIncoming { get; set; }
    public decimal Amount { get; set; }
    public PaymentChannel PaymentChannel { get; set; }
    public int? CashBoxId { get; set; }
    public CashBox? CashBox { get; set; }
    public int? BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    [MaxLength(250)]
    public string? Description { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeletedByUserName { get; set; }
}

/// <summary>
/// Fis/fatura fotografi gibi ekler. EntityType/EntityId ile herhangi bir kayda baglanir
/// (Asama 3'te yalnizca "LedgerTransaction"). Content sunucuda sikistirilmis JPEG olarak tutulur.
/// </summary>
public class Attachment : ISoftDeletable
{
    public int Id { get; set; }

    [Required, MaxLength(60)]
    public string EntityType { get; set; } = string.Empty;

    public int EntityId { get; set; }

    [Required, MaxLength(200)]
    public string FileName { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string ContentType { get; set; } = "image/jpeg";

    public int ByteSize { get; set; }

    public byte[] Content { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    [MaxLength(256)]
    public string? CreatedByUserName { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeletedByUserName { get; set; }
}

/// <summary>
/// Bir "mahsuplu gider" isleminin iki bacagini (aidat tahsilati + gider) birbirine baglar.
/// Collection/LedgerTransaction tablolarina alan eklenmez; iliski buradan izlenir.
/// </summary>
public class MahsupIslem : ISoftDeletable
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public Collection? Collection { get; set; }
    public int LedgerTransactionId { get; set; }
    public LedgerTransaction? LedgerTransaction { get; set; }
    public int UnitId { get; set; }
    public Unit? Unit { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    [MaxLength(256)]
    public string? CreatedByUserName { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeletedByUserName { get; set; }
}

public class BankAccount : ISoftDeletable
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? Branch { get; set; }

    [MaxLength(34)]
    public string? Iban { get; set; }

    public decimal OpeningBalance { get; set; }
    public DateTime OpeningBalanceDate { get; set; } = DateTime.Today;
    public bool Active { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeletedByUserName { get; set; }
}

public class CashBox : ISoftDeletable
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Name { get; set; } = "Kasa";

    public decimal OpeningBalance { get; set; }
    public DateTime OpeningBalanceDate { get; set; } = DateTime.Today;
    public bool Active { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }
    public string? DeletedByUserName { get; set; }
}

public class Announcement
{
    public int Id { get; set; }

    [Required, MaxLength(140)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(2000)]
    public string Body { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Priority { get; set; } = "Normal";

    public DateTime PublishDate { get; set; } = DateTime.UtcNow;
    public bool IsPublished { get; set; } = true;
}

public enum ServiceRequestStatus
{
    [Display(Name = "Açık")]
    Open = 1,
    [Display(Name = "İşlemde")]
    InProgress = 2,
    [Display(Name = "Çözüldü")]
    Resolved = 3,
    [Display(Name = "Kapalı")]
    Closed = 4
}

public enum ServiceRequestPriority
{
    [Display(Name = "Düşük")]
    Low = 1,
    [Display(Name = "Normal")]
    Normal = 2,
    [Display(Name = "Yüksek")]
    High = 3,
    [Display(Name = "Acil")]
    Urgent = 4
}

public class ServiceRequest
{
    public int Id { get; set; }

    [Required, MaxLength(140)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public int? UnitId { get; set; }
    public Unit? Unit { get; set; }

    public ServiceRequestStatus Status { get; set; } = ServiceRequestStatus.Open;
    public ServiceRequestPriority Priority { get; set; } = ServiceRequestPriority.Normal;

    [MaxLength(120)]
    public string? AssignedTo { get; set; }

    /// <summary>
    /// Atanan kullanicinin AspNetUsers.Id'si. AssignedTo (gorunen ad) eski kayitlar icin
    /// serbest metin olarak kalir; yeni atamalar bu alani da doldurup bildirim tetikler.
    /// </summary>
    [MaxLength(450)]
    public string? AssignedToUserId { get; set; }

    /// <summary>
    /// True ise talep Sakinler tarafından da görülebilir (mobil uygulamada listelenir).
    /// False ise yalnızca yetkili rollere görünür. Sakinin kendi açtığı talepler otomatik görünür.
    /// </summary>
    public bool IsVisibleToResidents { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public enum NotificationType
{
    TalepAtama = 1,
    TalepDurum = 2,
    Duyuru = 3,
    Sistem = 4
}

/// <summary>
/// Kullaniciya ozel bildirim (mobil zil). Push (Asama 5) bu tablodan bagimsiz best-effort calisir;
/// zil/rozet her zaman bu tablo uzerinden polling ile calisir.
/// </summary>
public class Notification
{
    public int Id { get; set; }

    [Required, MaxLength(450)]
    public string RecipientUserId { get; set; } = string.Empty;

    public NotificationType Type { get; set; }

    [Required, MaxLength(140)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Body { get; set; }

    [MaxLength(300)]
    public string? LinkUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null = okunmamis.</summary>
    public DateTime? ReadAt { get; set; }
}

public class DocumentRecord
{
    public int Id { get; set; }

    [Required, MaxLength(160)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Category { get; set; } = "Genel";

    [MaxLength(500)]
    public string? Url { get; set; }

    [MaxLength(2000)]
    public string? Note { get; set; }

    public DateTime DocumentDate { get; set; } = DateTime.UtcNow;
}

public class ReportLine
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Raporda hangi bölümde gösterileceği: Gelir veya Gider.</summary>
    [Required, MaxLength(20)]
    public string Section { get; set; } = "Gider";

    public int SortOrder { get; set; }
    public bool Visible { get; set; } = true;

    public List<ReportLineCategory> Categories { get; set; } = [];
    public List<ReportManualEntry> ManualEntries { get; set; } = [];
}

public class ReportLineCategory
{
    public int Id { get; set; }
    public int ReportLineId { get; set; }
    public ReportLine? ReportLine { get; set; }

    /// <summary>Null ise bu üye "Aidat Tahsilatı" kaynağını (Collections) temsil eder.</summary>
    public int? IncomeExpenseCategoryId { get; set; }
    public IncomeExpenseCategory? IncomeExpenseCategory { get; set; }

    public bool IsDuesCollections { get; set; }
}

public class ReportManualEntry
{
    public int Id { get; set; }

    /// <summary>Null ise raporda bağımsız satır olarak gösterilir; doluysa seçili rapor satırına dahil edilir.</summary>
    public int? ReportLineId { get; set; }
    public ReportLine? ReportLine { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Kalemin doğal tipi: Gelir veya Gider. Karışık satıra bağlanırsa karşıt tip netten düşülür.</summary>
    [Required, MaxLength(20)]
    public string Section { get; set; } = "Gelir";

    public DateTime EntryDate { get; set; }
    public decimal CashAmount { get; set; }
    public decimal BankAmount { get; set; }
    public int SortOrder { get; set; }
    public bool Visible { get; set; } = true;

    [MaxLength(250)]
    public string? Note { get; set; }
}
