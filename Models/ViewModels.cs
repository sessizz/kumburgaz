using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Kumburgaz.Web.Models;

public class BillingGroupFormViewModel
{
    public int? Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public int DuesTypeId { get; set; }

    [Required]
    [RegularExpression(@"^\d{4}-\d{4}$")]
    public string EffectiveStartPeriod { get; set; } = string.Empty;

    [RegularExpression(@"^\d{4}-\d{4}$")]
    public string? EffectiveEndPeriod { get; set; }

    public bool Active { get; set; } = true;
    public bool MergeUnits { get; set; }
    public List<int> SelectedUnitIds { get; set; } = [];
    public List<SelectListItem> DuesTypeOptions { get; set; } = [];
    public List<SelectListItem> UnitOptions { get; set; } = [];
}

public class UnitFormViewModel
{
    public int? Id { get; set; }

    [Required]
    public int BlockId { get; set; }

    [Required, MaxLength(30)]
    public string UnitNo { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? OwnerName { get; set; }

    public bool Active { get; set; } = true;
    public bool IsCombined { get; set; }
    public List<int> ComponentUnitIds { get; set; } = [];
    public List<SelectListItem> BlockOptions { get; set; } = [];
    public List<SelectListItem> ComponentUnitOptions { get; set; } = [];
}

public class DuesGenerationPreviewItem
{
    public int BillingGroupId { get; set; }
    public string BillingGroupName { get; set; } = string.Empty;
    public string DuesTypeName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string UnitsText { get; set; } = string.Empty;
}

public class DuesDebtReportQuery
{
    [RegularExpression(@"^\d{4}-\d{4}$")]
    public string? Period { get; set; }
    public int? BlockId { get; set; }
    public int? BillingGroupId { get; set; }
    public int? DuesTypeId { get; set; }
}

public class DuesDebtReportRow
{
    public int? InstallmentId { get; set; }
    public int BillingGroupId { get; set; }
    public string UnitDisplay { get; set; } = string.Empty;
    public string BillingGroupName { get; set; } = string.Empty;
    public string DuesTypeName { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public DateTime AccrualDate { get; set; }
    public decimal Amount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string UnitsText { get; set; } = string.Empty;
}

public class DuesListItemViewModel
{
    public int Id { get; set; }
    public string Period { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public string UnitNo { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string UnitDisplay { get; set; } = string.Empty;
    public string DuesTypeName { get; set; } = string.Empty;
    public DateTime AccrualDate { get; set; }
    public DateTime PaymentOrDueDate { get; set; }
    public bool IsPaid { get; set; }
    public bool IsOverdue { get; set; }
    public decimal Amount { get; set; }
    public decimal RemainingAmount { get; set; }
}

public class DuesIndexViewModel
{
    public List<DuesListItemViewModel> DuesItems { get; set; } = [];
    public List<Collection> Collections { get; set; } = [];
    public string Query { get; set; } = string.Empty;
    public string ActiveTab { get; set; } = "dues";
}

public class DuesInstallmentEditViewModel
{
    public int Id { get; set; }

    [Required]
    [RegularExpression(@"^\d{4}-\d{4}$")]
    public string Period { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Date)]
    public DateTime AccrualDate { get; set; } = DateTime.Today;

    [Required]
    [DataType(DataType.Date)]
    public DateTime DueDate { get; set; } = DateTime.Today;

    [Range(0.01, 999999999)]
    public decimal Amount { get; set; }

    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string UnitDisplay { get; set; } = string.Empty;
    public string BillingGroupName { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
}

public class CollectionCreateViewModel
{
    [Required]
    public int BillingGroupId { get; set; }

    public int? DuesInstallmentId { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.Today;

    [Range(1, 999999999)]
    public decimal Amount { get; set; }

    [Required]
    public PaymentChannel PaymentChannel { get; set; }

    [Required]
    public string? AccountKey { get; set; }

    public string? ReferenceNo { get; set; }
    public string? Note { get; set; }
    public string? ReturnUrl { get; set; }
    public List<SelectListItem> BillingGroupOptions { get; set; } = [];
    public List<SelectListItem> DuesInstallmentOptions { get; set; } = [];
    public List<SelectListItem> AccountOptions { get; set; } = [];
}

public class LedgerTransactionCreateViewModel
{
    [Required]
    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.Today;

    [Required]
    public int IncomeExpenseCategoryId { get; set; }

    [Range(1, 999999999)]
    public decimal Amount { get; set; }

    [Required]
    public PaymentChannel PaymentChannel { get; set; }

    [Required]
    public string? AccountKey { get; set; }

    public string? Description { get; set; }
    public List<SelectListItem> CategoryOptions { get; set; } = [];
    public List<SelectListItem> AccountOptions { get; set; } = [];
}

public class CashBoxFormViewModel
{
    [Required, MaxLength(80)]
    public string Name { get; set; } = "Kasa";

    public decimal OpeningBalance { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateTime OpeningBalanceDate { get; set; } = DateTime.Today;
}

public class BankAccountFormViewModel
{
    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? Branch { get; set; }

    [MaxLength(34)]
    public string? Iban { get; set; }

    public decimal OpeningBalance { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateTime OpeningBalanceDate { get; set; } = DateTime.Today;
}

public class CashBankListItemViewModel
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public decimal Balance { get; set; }
}

public class CashBankIndexViewModel
{
    public List<CashBankListItemViewModel> Items { get; set; } = [];
    public string Query { get; set; } = string.Empty;
    public decimal TotalBalance => Items.Sum(x => x.Balance);
    public CashBoxFormViewModel CashBoxForm { get; set; } = new();
    public BankAccountFormViewModel BankAccountForm { get; set; } = new();
}
