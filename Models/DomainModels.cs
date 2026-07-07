using System.ComponentModel.DataAnnotations;

namespace Kumburgaz.Web.Models;

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

public class Block
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public Site? Site { get; set; }

    [Required, MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    public ICollection<Unit> Units { get; set; } = new List<Unit>();
}

public class Unit
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

    public ICollection<BillingGroupUnit> BillingGroupUnits { get; set; } = new List<BillingGroupUnit>();
    public ICollection<UnitAccount> UnitAccounts { get; set; } = new List<UnitAccount>();
    public ICollection<CombinedUnitMember> CombinedUnitMembers { get; set; } = new List<CombinedUnitMember>();
    public ICollection<CombinedUnitMember> MemberOfCombinedUnits { get; set; } = new List<CombinedUnitMember>();
}

public class Account
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

    public ICollection<UnitAccount> UnitAccounts { get; set; } = new List<UnitAccount>();
    public ICollection<DuesInstallment> DuesInstallments { get; set; } = new List<DuesInstallment>();
}

public class UnitAccount
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
}

public class CombinedUnitMember
{
    public int Id { get; set; }
    public int CombinedUnitId { get; set; }
    public Unit? CombinedUnit { get; set; }
    public int ComponentUnitId { get; set; }
    public Unit? ComponentUnit { get; set; }
}

public class DuesType
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 999999999, ErrorMessage = "Tutar 0 ile 999.999.999 arasında olmalıdır.")]
    public decimal Amount { get; set; }

    public bool Active { get; set; } = true;

    public ICollection<BillingGroup> BillingGroups { get; set; } = new List<BillingGroup>();
}

public class BillingGroup
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

    public ICollection<BillingGroupUnit> Units { get; set; } = new List<BillingGroupUnit>();
    public ICollection<DuesInstallment> Installments { get; set; } = new List<DuesInstallment>();
}

public class BillingGroupUnit
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
}

public class DuesInstallment
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

    public ICollection<CollectionAllocation> Allocations { get; set; } = new List<CollectionAllocation>();
}

public class Collection
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

    public ICollection<CollectionAllocation> Allocations { get; set; } = new List<CollectionAllocation>();
}

public class CollectionAllocation
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public Collection? Collection { get; set; }
    public int DuesInstallmentId { get; set; }
    public DuesInstallment? DuesInstallment { get; set; }
    public decimal AppliedAmount { get; set; }
}

public class IncomeExpenseCategory
{
    public int Id { get; set; }

    [Required, MaxLength(60)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Type { get; set; } = "Expense";

    public bool Active { get; set; } = true;
}

public class LedgerTransaction
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
}

public class BankAccount
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
}

public class CashBox
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Name { get; set; } = "Kasa";

    public decimal OpeningBalance { get; set; }
    public DateTime OpeningBalanceDate { get; set; } = DateTime.Today;
    public bool Active { get; set; } = true;
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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public DateTime? ResolvedAt { get; set; }
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
