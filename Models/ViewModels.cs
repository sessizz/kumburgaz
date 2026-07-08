using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
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
    public DuesPayerType DuesPayerType { get; set; } = DuesPayerType.Owner;
    public decimal OpeningBalance { get; set; }
    public DateTime? OpeningBalanceDate { get; set; }
    [ValidateNever]
    public List<int>? ComponentUnitIds { get; set; }
    [ValidateNever]
    public List<SelectListItem> BlockOptions { get; set; } = [];
    [ValidateNever]
    public List<SelectListItem> OwnerAccountOptions { get; set; } = [];
    [ValidateNever]
    public List<SelectListItem> TenantAccountOptions { get; set; } = [];
    [ValidateNever]
    public List<SelectListItem> DuesPayerTypeOptions { get; set; } = [];
    [ValidateNever]
    public List<SelectListItem> ComponentUnitOptions { get; set; } = [];
}

public class UnitIndexViewModel
{
    public List<Unit> Units { get; set; } = [];
    public List<UnitBillingGroupSummaryItem> BillingGroupSummary { get; set; } = [];
}

public class UnitBillingGroupSummaryItem
{
    public string BillingGroupName { get; set; } = string.Empty;
    public string DuesTypeName { get; set; } = string.Empty;
    public int Count { get; set; }
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

public class AccountDetailViewModel
{
    public Account Account { get; set; } = null!;
    public List<AccountOpenInstallmentViewModel> OpenInstallments { get; set; } = [];
    public List<AccountCollectionRowViewModel> RecentCollections { get; set; } = [];
    public UnitLedgerSummary Summary { get; set; } = new();
}

public class AccountOpenInstallmentViewModel
{
    public int Id { get; set; }
    public int? UnitId { get; set; }
    public string Period { get; set; } = string.Empty;
    public string UnitDisplay { get; set; } = string.Empty;
    public string DuesTypeName { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public decimal RemainingAmount { get; set; }
    public bool IsOpeningBalance { get; set; }
}

public class AccountCollectionRowViewModel
{
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsOpeningBalance { get; set; }
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
    public string? BalanceStatus { get; set; }
}

public class CashBankStatementQuery
{
    public string? AccountKey { get; set; }

    [DataType(DataType.Date)]
    public DateTime? StartDate { get; set; }

    [DataType(DataType.Date)]
    public DateTime? EndDate { get; set; }
}

public class CashBankStatementViewModel
{
    public CashBankStatementQuery Query { get; set; } = new();
    public List<SelectListItem> AccountOptions { get; set; } = [];
    public string AccountName { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public decimal TotalIncome => Rows.Where(x => x.Amount > 0).Sum(x => x.Amount);
    public decimal TotalExpense => Rows.Where(x => x.Amount < 0).Sum(x => Math.Abs(x.Amount));
    public List<CashBankStatementRow> Rows { get; set; } = [];
}

public class CashBankStatementRow
{
    public DateTime Date { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal RunningBalance { get; set; }
}

public class BalanceReportQuery
{
    [DataType(DataType.Date)]
    public DateTime? StartDate { get; set; }

    [DataType(DataType.Date)]
    public DateTime? EndDate { get; set; }
}

public class BalanceReportViewModel
{
    public BalanceReportQuery Query { get; set; } = new();
    public decimal OpeningCash { get; set; }
    public decimal OpeningBank { get; set; }
    public decimal OpeningTotal => OpeningCash + OpeningBank;
    public decimal ClosingCash { get; set; }
    public decimal ClosingBank { get; set; }
    public decimal ClosingTotal => ClosingCash + ClosingBank;
    public decimal CarriedDebt { get; set; }
    public decimal CarriedCredit { get; set; }
    public List<BalanceCategoryTotal> IncomeRows { get; set; } = [];
    public List<BalanceCategoryTotal> ExpenseRows { get; set; } = [];
    public decimal IncomeTotal => IncomeRows.Sum(x => x.Amount);
    public decimal ExpenseTotal => ExpenseRows.Sum(x => x.Amount);
    public decimal Net => IncomeTotal - ExpenseTotal;
}

public class BalanceCategoryTotal
{
    public string CategoryName { get; set; } = string.Empty;
    public decimal DelayAmount { get; set; }
    public decimal Amount { get; set; }
}

public class MonthlyCashFlowViewModel
{
    public BalanceReportQuery Query { get; set; } = new();
    public List<MonthlyCashFlowRow> Rows { get; set; } = [];
    public decimal CollectionTotal => Rows.Sum(x => x.DuesCollections);
    public decimal OtherIncomeTotal => Rows.Sum(x => x.OtherIncome);
    public decimal ExpenseTotal => Rows.Sum(x => x.Expense);
    public decimal NetTotal => Rows.Sum(x => x.Net);
}

public class MonthlyCashFlowRow
{
    public DateTime Month { get; set; }
    public decimal DuesCollections { get; set; }
    public decimal OtherIncome { get; set; }
    public decimal Expense { get; set; }
    public decimal TotalIncome => DuesCollections + OtherIncome;
    public decimal Net => TotalIncome - Expense;
}

public class DebtAgingViewModel
{
    public DuesDebtReportQuery Query { get; set; } = new();
    public List<DebtAgingRow> Rows { get; set; } = [];
    public decimal CurrentTotal => Rows.Sum(x => x.Current);
    public decimal Days1To30Total => Rows.Sum(x => x.Days1To30);
    public decimal Days31To60Total => Rows.Sum(x => x.Days31To60);
    public decimal Days61To90Total => Rows.Sum(x => x.Days61To90);
    public decimal Over90Total => Rows.Sum(x => x.Over90);
    public decimal CreditTotal => Rows.Sum(x => x.Credit);
    public decimal DebtTotal => Rows.Sum(x => x.TotalDebt);
}

public class DebtAgingRow
{
    public int UnitId { get; set; }
    public string UnitDisplay { get; set; } = string.Empty;
    public string ResponsibleAccountName { get; set; } = string.Empty;
    public decimal Current { get; set; }
    public decimal Days1To30 { get; set; }
    public decimal Days31To60 { get; set; }
    public decimal Days61To90 { get; set; }
    public decimal Over90 { get; set; }
    public decimal Credit { get; set; }
    public decimal TotalDebt => Current + Days1To30 + Days31To60 + Days61To90 + Over90;
    public decimal NetBalance => TotalDebt - Credit;
}

public class DuesDebtReportRow
{
    public int? InstallmentId { get; set; }
    public int? UnitId { get; set; }
    public int BillingGroupId { get; set; }
    public string UnitDisplay { get; set; } = string.Empty;
    public string BillingGroupName { get; set; } = string.Empty;
    public string DuesTypeName { get; set; } = string.Empty;
    public string ResponsibleAccountName { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public DateTime AccrualDate { get; set; }
    public decimal Amount { get; set; }
    public decimal RemainingAmount { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal UnallocatedCredit { get; set; }
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

public enum UnitLedgerEntryKind
{
    OpeningBalance,
    DuesAccrual,
    Collection,
    AdvanceCredit,
    ManualAdjustment
}

public class UnitLedgerEntry
{
    public UnitLedgerEntryKind Kind { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal RunningBalance { get; set; }
    public int? SourceId { get; set; }
}

public class UnitLedgerSummary
{
    public decimal TotalAccrual { get; set; }
    public decimal TotalCollections { get; set; }
    public decimal OpeningCredit { get; set; }
    public decimal OpeningDebt { get; set; }
    public decimal NetBalance { get; set; }
    public decimal Advance => NetBalance < 0 ? Math.Abs(NetBalance) : 0m;
    public decimal Debt => NetBalance > 0 ? NetBalance : 0m;
}

public class UnitLedgerResult
{
    public Unit Unit { get; set; } = null!;
    public List<UnitLedgerEntry> Entries { get; set; } = [];
    public UnitLedgerSummary Summary { get; set; } = new();
}

public class UnitDetailViewModel
{
    public Unit Unit { get; set; } = null!;
    public List<StatementEntry> RecentEntries { get; set; } = [];
    /// <summary>Toplam borç (yapılacak tahsilat); negatif ise daire alacaklı.</summary>
    public decimal Balance { get; set; }
    public StatementEntry? LastDebt { get; set; }
    public UnitLedgerSummary Summary { get; set; } = new();
}

public class UnitStatementViewModel
{
    public Unit Unit { get; set; } = null!;
    public List<StatementEntry> Entries { get; set; } = [];
    public decimal Balance { get; set; }
    public UnitLedgerSummary Summary { get; set; } = new();
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

public class LedgerIndexViewModel
{
    public int? CategoryId { get; set; }
    public List<int> CategoryIds { get; set; } = [];
    [DataType(DataType.Date)]
    public DateTime? StartDate { get; set; }
    [DataType(DataType.Date)]
    public DateTime? EndDate { get; set; }
    public List<SelectListItem> CategoryOptions { get; set; } = [];
    public List<LedgerTransaction> Rows { get; set; } = [];
    public List<LedgerCategorySummaryRow> CategorySummaryRows { get; set; } = [];
    public decimal TotalAmount => Rows.Sum(x => x.Amount);
    public decimal CashAmount => Rows.Where(x => x.PaymentChannel == PaymentChannel.Cash).Sum(x => x.Amount);
    public decimal BankAmount => Rows.Where(x => x.PaymentChannel == PaymentChannel.Bank).Sum(x => x.Amount);
    public decimal AverageAmount => Rows.Count == 0 ? 0 : TotalAmount / Rows.Count;
    public decimal MaxAmount => Rows.Count == 0 ? 0 : Rows.Max(x => x.Amount);
}

public class LedgerCategorySummaryRow
{
    public int? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal TotalAmount { get; set; }
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

public class CashBankAccountEditViewModel
{
    [Required]
    public string Kind { get; set; } = "bank";

    [Required]
    public int Id { get; set; }

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

public class CashBankOpeningBalanceViewModel
{
    [Required]
    public string Kind { get; set; } = "bank";

    [Required]
    public int Id { get; set; }

    public decimal OpeningBalance { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateTime OpeningBalanceDate { get; set; } = DateTime.Today;
}

public class CashBankCollectionFormViewModel
{
    [Required]
    public string Kind { get; set; } = "bank";

    [Required]
    public int Id { get; set; }

    [Required]
    public int? DuesInstallmentId { get; set; }

    public int BillingGroupId { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.Today;

    [Range(1, 999999999)]
    public decimal Amount { get; set; }

    [MaxLength(80)]
    public string? ReferenceNo { get; set; }

    [MaxLength(250)]
    public string? Note { get; set; }
}

public class CashBankDuesOptionViewModel
{
    public int Id { get; set; }
    public int? UnitId { get; set; }
    public int BillingGroupId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string SearchText { get; set; } = string.Empty;
    public decimal RemainingAmount { get; set; }
}

public class CashBankImportPreviewViewModel
{
    public string Kind { get; set; } = "bank";
    public int Id { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public List<CashBankImportRowViewModel> Rows { get; set; } = [];
    public List<CashBankDuesOptionViewModel> DuesOptions { get; set; } = [];
    public List<SelectListItem> IncomeCategoryOptions { get; set; } = [];
    public List<SelectListItem> ExpenseCategoryOptions { get; set; } = [];
    public List<SelectListItem> TransferAccountOptions { get; set; } = [];
}

public class CashBankImportRowViewModel
{
    public bool Include { get; set; } = true;
    public int LineNo { get; set; }
    public string Type { get; set; } = "collection";
    public string Date { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
    public string Amount { get; set; } = string.Empty;
    public int? DuesInstallmentId { get; set; }
    public int? ExpenseCategoryId { get; set; }
    public string? ToAccountKey { get; set; }
    public bool TransferToCurrentAccount { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public string ReferenceNo { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class CashBankLedgerFormViewModel
{
    [Required]
    public string Kind { get; set; } = "bank";

    [Required]
    public int Id { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.Today;

    [Required]
    public int IncomeExpenseCategoryId { get; set; }

    [Range(1, 999999999)]
    public decimal Amount { get; set; }

    [MaxLength(250)]
    public string? Description { get; set; }

    public bool IsBankFee { get; set; }
}

public class CashBankTransferFormViewModel
{
    [Required]
    public string Kind { get; set; } = "bank";

    [Required]
    public int Id { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.Today;

    [Range(1, 999999999)]
    public decimal Amount { get; set; }

    [Required]
    public string? ToAccountKey { get; set; }

    [MaxLength(250)]
    public string? Description { get; set; }
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
    public string Source { get; set; } = "";
    public string AccountKind { get; set; } = "";
    public int AccountId { get; set; }
    public int? UnitId { get; set; }
    public int? BillingGroupId { get; set; }
    public int? DuesInstallmentId { get; set; }
    public int? IncomeExpenseCategoryId { get; set; }
    public string? ReferenceNo { get; set; }
    public string? Note { get; set; }
    public string? ToAccountKey { get; set; }
    public string Description { get; set; } = "";
    public string? Subline { get; set; }
    public TxKind Kind { get; set; }
    public decimal Amount { get; set; }
    public decimal RunningBalance { get; set; }
    public DateTime Date { get; set; }
    public List<CashBankDuesOptionViewModel> DuesOptions { get; set; } = [];
    public List<SelectListItem> IncomeCategoryOptions { get; set; } = [];
    public List<SelectListItem> ExpenseCategoryOptions { get; set; } = [];
    public List<SelectListItem> TransferAccountOptions { get; set; } = [];
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
    public List<CashBankDuesOptionViewModel> DuesOptions { get; set; } = [];
    public List<SelectListItem> IncomeCategoryOptions { get; set; } = [];
    public List<SelectListItem> ExpenseCategoryOptions { get; set; } = [];
    public List<SelectListItem> TransferAccountOptions { get; set; } = [];
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
    public string Basis { get; set; } = string.Empty;
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
    public decimal OverdueCarriedDebt { get; set; }
    public decimal OverdueDuesDebt { get; set; }
    public decimal ForecastExpense { get; set; }
    public decimal MonthCollections { get; set; }
    public int MonthCollectionCount { get; set; }
    public decimal CashBankBalance { get; set; }
    public decimal NetPosition { get; set; }
    public int ActiveUnits { get; set; }
    public int OpenRequestCount { get; set; }
    public int ForecastConfidence { get; set; }
    public string ForecastMonthLabel { get; set; } = string.Empty;
    public List<ExpenseForecastItem> ExpenseForecast { get; set; } = [];
    public List<DashboardCashflowMonth> Cashflow { get; set; } = [];
    public List<DashboardOverdueItem> OverdueItems { get; set; } = [];
    public List<DashboardUpcomingExpense> UpcomingExpenses { get; set; } = [];
    public List<ServiceRequest> RecentRequests { get; set; } = [];
    public List<Announcement> RecentAnnouncements { get; set; } = [];
    public List<DashboardCalendarDay> CalendarDays { get; set; } = [];
    public List<string> Alerts { get; set; } = [];
    public DateTime? LastBackupAt { get; set; }
}

public class BalanceDetailedQuery
{
    [DataType(DataType.Date)]
    public DateTime? StartDate { get; set; }

    [DataType(DataType.Date)]
    public DateTime? EndDate { get; set; }
}

public class BalanceDetailedRow
{
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public decimal Cash { get; set; }
    public decimal Bank { get; set; }
    public decimal Total => Cash + Bank;
    public bool IsAuto { get; set; }
    public bool IsManual { get; set; }
    public string MembersText { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public class BalanceDetailedViewModel
{
    public BalanceDetailedQuery Query { get; set; } = new();
    public List<BalanceDetailedRow> IncomeRows { get; set; } = [];
    public List<BalanceDetailedRow> ExpenseRows { get; set; } = [];
    public int HiddenLineCount { get; set; }
    public decimal HiddenTotal { get; set; }
    public decimal IncomeCash => IncomeRows.Sum(x => x.Cash);
    public decimal IncomeBank => IncomeRows.Sum(x => x.Bank);
    public decimal IncomeTotal => IncomeRows.Sum(x => x.Total);
    public decimal ExpenseCash => ExpenseRows.Sum(x => x.Cash);
    public decimal ExpenseBank => ExpenseRows.Sum(x => x.Bank);
    public decimal ExpenseTotal => ExpenseRows.Sum(x => x.Total);
    public decimal Net => IncomeTotal - ExpenseTotal;
}

public class ReportLineListItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool Visible { get; set; }
    public string MembersText { get; set; } = string.Empty;
}

public class ReportManualEntryListItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Section { get; set; } = string.Empty;
    public DateTime EntryDate { get; set; }
    public decimal CashAmount { get; set; }
    public decimal BankAmount { get; set; }
    public decimal Total => CashAmount + BankAmount;
    public int SortOrder { get; set; }
    public bool Visible { get; set; }
    public string? ReportLineName { get; set; }
    public string? Note { get; set; }
}

public class ReportLineFormViewModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Görünen ad zorunludur."), MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Section { get; set; } = "Gider";

    public bool Visible { get; set; } = true;
    public int SortOrder { get; set; }

    public List<string> SelectedKeys { get; set; } = [];

    [ValidateNever]
    public List<ReportLineCategoryOption> Options { get; set; } = [];
}

public class ReportLineCategoryOption
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? CurrentLineName { get; set; }
}

public class ReportManualEntryFormViewModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Görünen ad zorunludur."), MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Section { get; set; } = "Gelir";

    [Required]
    [DataType(DataType.Date)]
    public DateTime EntryDate { get; set; } = DateTime.Today;

    [Range(0, 999999999)]
    public decimal CashAmount { get; set; }

    [Range(0, 999999999)]
    public decimal BankAmount { get; set; }

    public int SortOrder { get; set; }
    public bool Visible { get; set; } = true;
    public int? ReportLineId { get; set; }

    [MaxLength(250)]
    public string? Note { get; set; }

    [ValidateNever]
    public List<SelectListItem> ReportLineOptions { get; set; } = [];
}

public class AuditIndexViewModel
{
    public List<AuditLogRowViewModel> AuditLogs { get; set; } = [];
    public List<ImportBatch> ImportBatches { get; set; } = [];
    public List<ConsistencyIssueRowViewModel> ConsistencyIssues { get; set; } = [];
}

public class AuditLogRowViewModel
{
    public AuditLog Log { get; set; } = null!;
    public string RecordTitle { get; set; } = string.Empty;
    public string DetailSummary { get; set; } = string.Empty;
    public string? DetailUrl { get; set; }
    public string RestoreConfirmText { get; set; } = string.Empty;
}

public class ConsistencyIssueRowViewModel
{
    public ConsistencyCheckResult Issue { get; set; } = null!;
    public string EntityTitle { get; set; } = string.Empty;
    public string DetailSummary { get; set; } = string.Empty;
    public string? DetailUrl { get; set; }
    public string? SecondaryUrl { get; set; }
    public string? SecondaryText { get; set; }
}

public class BackupFileViewModel
{
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BackupIndexViewModel
{
    public string Directory { get; set; } = string.Empty;
    public DateTime? LastBackupAt { get; set; }
    public List<BackupFileViewModel> Files { get; set; } = [];
}
