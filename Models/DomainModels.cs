using System.ComponentModel.DataAnnotations;

namespace Kumburgaz.Web.Models;

public enum PaymentChannel
{
    Cash = 1,
    Bank = 2
}

public enum InstallmentStatus
{
    Open = 1,
    PartiallyPaid = 2,
    Paid = 3
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

    public bool Active { get; set; } = true;

    public ICollection<BillingGroupUnit> BillingGroupUnits { get; set; } = new List<BillingGroupUnit>();
}

public class DuesType
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 999999999)]
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

    [Required, MaxLength(9)]
    public string Period { get; set; } = string.Empty; // YYYY-YYYY

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
    public int IncomeExpenseCategoryId { get; set; }
    public IncomeExpenseCategory? IncomeExpenseCategory { get; set; }
    public decimal Amount { get; set; }
    public PaymentChannel PaymentChannel { get; set; }

    [MaxLength(250)]
    public string? Description { get; set; }
}

public class BankAccount
{
    public int Id { get; set; }

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(34)]
    public string? Iban { get; set; }

    public decimal OpeningBalance { get; set; }
    public bool Active { get; set; } = true;
}

public class CashBox
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Name { get; set; } = "Kasa";

    public decimal OpeningBalance { get; set; }
    public bool Active { get; set; } = true;
}
