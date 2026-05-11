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

    [MaxLength(40)]
    public string? Phone { get; set; }

    public DateTime? MoveInDate { get; set; }
    public int? OwnerAccountId { get; set; }
    public int? TenantAccountId { get; set; }

    public bool Active { get; set; } = true;
    public bool IsCombined { get; set; }
    public decimal OpeningBalance { get; set; }
    public DateTime? OpeningBalanceDate { get; set; }
    public List<int> ComponentUnitIds { get; set; } = [];
    public List<SelectListItem> BlockOptions { get; set; } = [];
    public List<SelectListItem> OwnerAccountOptions { get; set; } = [];
    public List<SelectListItem> TenantAccountOptions { get; set; } = [];
    public List<SelectListItem> ComponentUnitOptions { get; set; } = [];
}

public class AccountFormViewModel
{
    public int? Id { get; set; }

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public AccountType AccountType { get; set; } = AccountType.Owner;

    [MaxLength(40)]
    public string? Phone { get; set; }

    [MaxLength(160), EmailAddress]
    public string? Email { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public bool Active { get; set; } = true;
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
    public string ResponsibleAccountName { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public DateTime AccrualDate { get; set; }
    public decimal Amount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string UnitsText { get; set; } = string.Empty;
}

public class DuesListItemViewModel
{
    public int Id { get; set; }
    public int? UnitId { get; set; }
    public string Period { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public string UnitNo { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string ResponsibleAccountName { get; set; } = string.Empty;
    public string UnitDisplay { get; set; } = string.Empty;
    public string DuesTypeName { get; set; } = string.Empty;
    public DateTime AccrualDate { get; set; }
    public DateTime PaymentOrDueDate { get; set; }
    /// <summary>Bu taksite tahsis edilmiş gerçek son tahsilat tarihi (varsa).</summary>
    public DateTime? LastPaymentDate { get; set; }
    public bool IsPaid { get; set; }
    public bool IsOverdue { get; set; }
    public decimal Amount { get; set; }
    public decimal RemainingAmount { get; set; }
    /// <summary>True ise bu satır bir aidat taksiti değil, devir bakiyesidir (Tahsilat butonu gizlenir).</summary>
    public bool IsOpeningBalance { get; set; }
}

public class DuesIndexViewModel
{
    public List<DuesListItemViewModel> DuesItems { get; set; } = [];
    public List<Collection> Collections { get; set; } = [];
    public string Query { get; set; } = string.Empty;
    public string ActiveTab { get; set; } = "dues";
}

public enum StatementEntryKind
{
    OpeningBalance,
    Debt,
    Collection
}

public class StatementEntry
{
    public StatementEntryKind Kind { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>Pozitif = borç, negatif = tahsilat.</summary>
    public decimal Amount { get; set; }
    public decimal RunningBalance { get; set; }
}

public class UnitDetailViewModel
{
    public Unit Unit { get; set; } = null!;
    public List<StatementEntry> RecentEntries { get; set; } = [];
    /// <summary>Toplam borç (yapılacak tahsilat); negatif ise daire alacaklı.</summary>
    public decimal Balance { get; set; }
    public StatementEntry? LastDebt { get; set; }
}

public class UnitStatementViewModel
{
    public Unit Unit { get; set; } = null!;
    public List<StatementEntry> Entries { get; set; } = [];
    public decimal Balance { get; set; }
}

public class AddCollectionModalModel
{
    public int UnitId { get; set; }
    public decimal SuggestedAmount { get; set; }
    public string ReturnUrl { get; set; } = string.Empty;
    public List<SelectListItem> AccountOptions { get; set; } = [];
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
    public string ResponsibleAccountName { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
}

public class DuesInstallmentCreateViewModel
{
    [Required]
    public int BillingGroupId { get; set; }

    [Required]
    public int UnitId { get; set; }

    [Required]
    public DuesPayerType PayerType { get; set; } = DuesPayerType.Owner;

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

    public List<SelectListItem> BillingGroupOptions { get; set; } = [];
    public List<SelectListItem> UnitOptions { get; set; } = [];
    public List<SelectListItem> PayerTypeOptions { get; set; } = [];
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
    public int Id { get; set; }
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

public class CashBankDetailQuery
{
    public string? Q { get; set; }
    public string? Type { get; set; } = "all";
    public string? Range { get; set; } = "all";
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public enum TxKind { Tahsilat, Cikis, Transfer, Girdi, Acilis }

public class TxRow
{
    public int Id { get; set; }
    public string Description { get; set; } = "";
    public string? Subline { get; set; }
    public TxKind Kind { get; set; }
    public decimal Amount { get; set; }
    public decimal RunningBalance { get; set; }
    public DateTime Date { get; set; }
}

public class TxDayGroup
{
    public DateOnly Date { get; set; }
    public decimal Net { get; set; }
    public IReadOnlyList<TxRow> Items { get; set; } = Array.Empty<TxRow>();
}

public class AuditEntry
{
    public string Action { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime At { get; set; }
    public string ByUserName { get; set; } = "";
}

public class CashBankDetailViewModel
{
    public string Kind { get; set; } = "bank";
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Branch { get; set; }
    public string? Iban { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal OpeningBalance { get; set; }
    public DateOnly OpeningDate { get; set; }
    public decimal Balance { get; set; }
    public DateTime? LastTransactionAt { get; set; }
    public decimal MonthInflow { get; set; }
    public int MonthInflowCount { get; set; }
    public decimal MonthOutflow { get; set; }
    public int MonthOutflowCount { get; set; }
    public IReadOnlyList<int> Last14DaysActivity { get; set; } = Array.Empty<int>();
    public CashBankDetailQuery Query { get; set; } = new();
    public int TotalCount { get; set; }
    public int TahsilatCount { get; set; }
    public int CikisCount { get; set; }
    public int TransferCount { get; set; }
    public IReadOnlyList<TxDayGroup> Groups { get; set; } = Array.Empty<TxDayGroup>();
    public IReadOnlyList<AuditEntry> History { get; set; } = Array.Empty<AuditEntry>();
    public int PendingCount { get; set; }
    public string? Note { get; set; }
}

public class DashboardMetricViewModel
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Subtext { get; set; } = string.Empty;
    public string Icon { get; set; } = "monitoring";
    public string Tone { get; set; } = "blue";
}

public class ExpenseForecastItem
{
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Percent { get; set; }
    public string Color { get; set; } = "#3b82f6";
}

public class DashboardOverdueItem
{
    public string UnitDisplay { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Days { get; set; }
}

public class DashboardUpcomingExpense
{
    public string Name { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Bekliyor";
}

public class DashboardCashflowMonth
{
    public string Month { get; set; } = string.Empty;
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal Net => Income - Expense;
}

public class DashboardCalendarDay
{
    public string Weekday { get; set; } = string.Empty;
    public int Day { get; set; }
    public string Marker { get; set; } = "b";
    public bool IsToday { get; set; }
}

public class DashboardViewModel
{
    public decimal CollectionRate { get; set; }
    public decimal TotalGenerated { get; set; }
    public decimal OverdueDebt { get; set; }
    public int OverdueUnitCount { get; set; }
    public decimal ForecastExpense { get; set; }
    public decimal MonthCollections { get; set; }
    public int MonthCollectionCount { get; set; }
    public decimal CashBankBalance { get; set; }
    public decimal NetPosition { get; set; }
    public int ActiveUnits { get; set; }
    public int OpenRequestCount { get; set; }
    public List<ExpenseForecastItem> ExpenseForecast { get; set; } = [];
    public List<DashboardCashflowMonth> Cashflow { get; set; } = [];
    public List<DashboardOverdueItem> OverdueItems { get; set; } = [];
    public List<DashboardUpcomingExpense> UpcomingExpenses { get; set; } = [];
    public List<ServiceRequest> RecentRequests { get; set; } = [];
    public List<Announcement> RecentAnnouncements { get; set; } = [];
    public List<DashboardCalendarDay> CalendarDays { get; set; } = [];
}
