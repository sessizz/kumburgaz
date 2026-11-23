using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Kumburgaz.Web.Mcp.Tools;

public sealed record AccountBalance(string Key, string Name, decimal Balance);

public sealed record SiteSummary(
    decimal CashTotal,
    decimal BankTotal,
    List<AccountBalance> Accounts,
    decimal? DuesDebtTotal,
    decimal OverdueDebtTotal,
    decimal CollectionsThisMonth,
    decimal ExpensesThisMonth,
    decimal IncomeThisMonth);

public sealed record CollectionRow(
    int Id, string Date, string Unit, decimal Amount, string Account, string? ReferenceNo, bool IsReceipt, string Link);

public sealed record CollectionsResult(decimal Total, int Count, List<CollectionRow> Rows);

public sealed record CashBankAccountRow(string Key, string Type, string Name, decimal Balance, string Link);

public sealed record CashBankTransactionRow(string Date, string Description, decimal Inflow, decimal Outflow, decimal Balance);

public sealed record CashBankTransactionsResult(
    string Account,
    decimal Opening,
    decimal TotalInflow,
    decimal TotalOutflow,
    decimal Closing,
    int Count,
    List<CashBankTransactionRow> Rows,
    string Link);

public sealed record LedgerRow(
    int Id, string Date, string Kind, string Category, decimal Amount, string Account, string? Description, string Link);

public sealed record LedgerResult(decimal Total, int Count, List<LedgerRow> Rows);

[McpServerToolType]
public class FinanceTools(
    ApplicationDbContext db,
    PermissionService permissions,
    IReportingService reporting,
    DashboardFiguresService figures,
    CashBankDetailService cashBankDetail,
    McpLinks links)
{
    private const string Income = "income";
    private const string Expense = "expense";
    private const string DateFormat = "yyyy-MM-dd";
    private const string BadAccountKey = "Geçerli bir kasa/banka hesabı belirtin (cash:{id} ya da bank:{id}).";
    private const string BadRange = "from, to'dan sonra olamaz.";

    [McpServerTool(Name = "site_summary", Title = "Site özeti", ReadOnly = true)]
    [Description("Panel ekranındaki özet: kasa/banka bakiyeleri, aidat borcu, vadesi geçmiş borç ve bu ayın tahsilat/gelir/gider toplamları (TL). "
        + "Bu ay = UTC takvim ayı, Panel ile aynı. duesDebtTotal: Daire Borç/Alacak raporundaki toplam borç. "
        + "overdueDebtTotal: Panel'deki vadesi geçmiş borç (devir borcu + vadesi geçen aidat). "
        + "collectionsThisMonth: aidat tahsilatları. incomeThisMonth: aidat tahsilatları HARİÇ diğer gelirler (Panel'deki \"diğer gelir\"). "
        + "expensesThisMonth: gider toplamı. "
        + "accounts (hesap bazlı döküm) yalnız Kasa/Banka görüntüleme yetkisi olana dolar, yoksa boş liste gelir; "
        + "duesDebtTotal yalnız Raporlar yetkisi olana gelir, yoksa null.")]
    public async Task<SiteSummary> SiteSummaryAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        McpAccess.Require(user, permissions, AppModules.Panel, write: false);

        var balances = await CashBankBalanceHelper.BuildAsync(db);
        // Panel yalnız toplamı gösterir; hesap dökümü KasaBanka, aidat borcu raporu Raporlar verisidir.
        decimal? duesDebt = permissions.CanView(user, AppModules.Raporlar)
            ? DuesDebtSummaryHelper.Build(await reporting.GetDuesDebtReportAsync(new())).TotalDebt
            : null;
        var overdue = await figures.GetOverdueDebtAsync();
        var month = await figures.GetMonthFiguresAsync(DashboardFiguresService.MonthStartUtc(DateTime.UtcNow));

        return new SiteSummary(
            balances.Where(x => x.Type == "cash").Sum(x => x.Balance),
            balances.Where(x => x.Type == "bank").Sum(x => x.Balance),
            permissions.CanView(user, AppModules.KasaBanka)
                ? balances.Select(x => new AccountBalance(Key(x), x.Name, x.Balance)).ToList()
                : [],
            duesDebt,
            overdue.Total,
            month.Collections,
            month.Expense,
            month.OtherIncome);
    }

    [McpServerTool(Name = "collections", Title = "Tahsilatlar", ReadOnly = true)]
    [Description("Tahsilatları en yeni önce listeler. from/to dahil takvim günleridir (YYYY-AA-GG). accountKey: cash:{id} ya da bank:{id}. "
        + "total: eşleşen tüm tahsilatların toplamı (TL, limit'ten bağımsız); count: eşleşen tahsilat sayısı.")]
    public async Task<CollectionsResult> CollectionsAsync(
        ClaimsPrincipal user,
        [Description("Başlangıç tarihi, YYYY-AA-GG (dahil).")] string? from = null,
        [Description("Bitiş tarihi, YYYY-AA-GG (dahil).")] string? to = null,
        [Description("Yalnız bu daire (daire id'si).")] int? unitId = null,
        [Description("Yalnız bu kasa/banka: cash:{id} ya da bank:{id} (cash_bank_accounts sonucundan).")] string? accountKey = null,
        [Description("En fazla kaç satır (varsayılan 100, en çok 500).")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        McpAccess.Require(user, permissions, AppModules.Tahsilatlar, write: false);

        var (fromDate, toExclusive) = ParseRange(from, to);
        var query = db.Collections.AsNoTracking().AsQueryable();
        if (fromDate is { } f)
        {
            query = query.Where(x => x.Date >= f);
        }

        if (toExclusive is { } t)
        {
            query = query.Where(x => x.Date < t);
        }

        if (unitId.HasValue)
        {
            query = query.Where(x => x.UnitId == unitId.Value);
        }

        if (accountKey is not null)
        {
            var (cashBoxId, bankAccountId) = ParseAccountKey(accountKey);
            query = cashBoxId.HasValue
                ? query.Where(x => x.CashBoxId == cashBoxId)
                : query.Where(x => x.BankAccountId == bankAccountId);
        }

        var total = await query.SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
        var count = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .Take(McpInput.ClampLimit(limit))
            .Include(x => x.Unit).ThenInclude(x => x!.Block)
            .Include(x => x.CashBox)
            .Include(x => x.BankAccount)
            .ToListAsync(cancellationToken);

        return new CollectionsResult(
            total,
            count,
            rows.Select(x => new CollectionRow(
                x.Id,
                FormatDate(x.Date),
                x.Unit is null ? string.Empty : UnitDisplayHelper.Display(x.Unit),
                x.Amount,
                x.CashBox?.Name ?? (x.BankAccount is null ? string.Empty : FinancialAccountHelper.BankName(x.BankAccount)),
                x.ReferenceNo,
                x.IsReceipt,
                links.Collection(x.Id))).ToList());
    }

    [McpServerTool(Name = "cash_bank_accounts", Title = "Kasa ve banka hesapları", ReadOnly = true)]
    [Description("Aktif kasa ve banka hesaplarını güncel bakiyeleriyle listeler. key (cash:{id} / bank:{id}) diğer araçlarda accountKey olarak kullanılır.")]
    public async Task<List<CashBankAccountRow>> CashBankAccountsAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        McpAccess.Require(user, permissions, AppModules.KasaBanka, write: false);

        var balances = await CashBankBalanceHelper.BuildAsync(db);
        return balances.Select(x => new CashBankAccountRow(Key(x), x.Type, x.Name, x.Balance, links.CashBank(Key(x)))).ToList();
    }

    [McpServerTool(Name = "cash_bank_transactions", Title = "Kasa/banka hareketleri", ReadOnly = true)]
    [Description("Bir kasa/banka hesabının hareketlerini en yeni önce, yürüyen bakiyeyle listeler (tahsilat, gelir/gider ve transferler). "
        + "from/to dahil takvim günleridir (YYYY-AA-GG). inflow/outflow: giriş/çıkış (TL); balance: o harekete kadarki hesap bakiyesi (tüm geçmişi kapsar). "
        + "opening: aralığın ilk hareketinden hemen önceki bakiye (from verilmezse hesabın açılış bakiyesi). closing: seçilen bitiş gününün sonundaki bakiye (to verilmezse güncel bakiye); "
        + "totalInflow/totalOutflow: eşleşen tüm hareketlerin giriş/çıkış toplamı; opening + totalInflow - totalOutflow = closing. "
        + "count: eşleşen hareket sayısı. Toplamlar ve count limit'ten bağımsızdır; rows limit'le kesilebilir, toplamı satırlardan çıkarmayın.")]
    public async Task<CashBankTransactionsResult> CashBankTransactionsAsync(
        ClaimsPrincipal user,
        [Description("Hesap anahtarı: cash:{id} ya da bank:{id} (cash_bank_accounts sonucundan).")] string accountKey,
        [Description("Başlangıç tarihi, YYYY-AA-GG (dahil).")] string? from = null,
        [Description("Bitiş tarihi, YYYY-AA-GG (dahil).")] string? to = null,
        [Description("En fazla kaç satır (varsayılan 100, en çok 500).")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        McpAccess.Require(user, permissions, AppModules.KasaBanka, write: false);

        var (cashBoxId, bankAccountId) = ParseAccountKey(accountKey);
        var (fromDate, toExclusive) = ParseRange(from, to);

        var kind = cashBoxId.HasValue ? "cash" : "bank";
        var detail = await cashBankDetail.BuildAsync(kind, cashBoxId ?? bankAccountId!.Value,
            new CashBankDetailQuery { Range = "all", Type = "all", Page = 1, PageSize = int.MaxValue })
            ?? throw new McpException(BadAccountKey);

        // Servis yürüyen bakiyeyi açılış satırı önde, sonra tarih/id sırasıyla hesaplar.
        var ordered = detail.Groups.SelectMany(x => x.Items)
            .OrderBy(x => x.Kind == TxKind.Acilis ? 0 : 1)
            .ThenBy(x => x.Date)
            .ThenBy(x => x.Id)
            .ToList();

        var closing = toExclusive is null
            ? detail.Balance
            : ordered.Where(x => x.Kind == TxKind.Acilis || x.Date < toExclusive).Select(x => x.RunningBalance).LastOrDefault();

        // opening: aralığın ilk satırından hemen önceki bakiye; from yoksa hesabın açılış bakiyesi.
        var opening = fromDate is null
            ? detail.OpeningBalance
            : ordered.Where(x => x.Kind == TxKind.Acilis || x.Date < fromDate).Select(x => x.RunningBalance).LastOrDefault();

        var matching = ordered
            .Where(x => x.Kind != TxKind.Acilis
                        && (fromDate is null || x.Date >= fromDate)
                        && (toExclusive is null || x.Date < toExclusive))
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .ToList();

        var key = cashBoxId.HasValue ? FinancialAccountHelper.CashKey(cashBoxId.Value) : FinancialAccountHelper.BankKey(bankAccountId!.Value);
        return new CashBankTransactionsResult(
            string.IsNullOrWhiteSpace(detail.Branch) || detail.Kind != "bank" ? detail.Name : $"{detail.Name} - {detail.Branch}",
            opening,
            matching.Sum(x => Math.Max(x.Amount, 0m)),
            matching.Sum(x => Math.Max(-x.Amount, 0m)),
            closing,
            matching.Count,
            matching.Take(McpInput.ClampLimit(limit))
                .Select(x => new CashBankTransactionRow(
                    FormatDate(x.Date), x.Description, Math.Max(x.Amount, 0m), Math.Max(-x.Amount, 0m), x.RunningBalance))
                .ToList(),
            links.CashBank(key));
    }

    [McpServerTool(Name = "ledger", Title = "Gelir/gider defteri", ReadOnly = true)]
    [Description("Gerçek gelir ve gider kayıtlarını (transferler hariç, tahsilatlar hariç) en yeni önce listeler. "
        + "kind: \"income\" ya da \"expense\". from/to dahil takvim günleridir (YYYY-AA-GG). categoryId için categories aracını kullanın. "
        + "total: eşleşen tüm kayıtların toplamı (TL, limit'ten bağımsız); count: eşleşen kayıt sayısı.")]
    public async Task<LedgerResult> LedgerAsync(
        ClaimsPrincipal user,
        [Description("\"income\" (gelir) ya da \"expense\" (gider). Verilmezse ikisi de.")] string? kind = null,
        [Description("Yalnız bu kategori (categories sonucundan).")] int? categoryId = null,
        [Description("Başlangıç tarihi, YYYY-AA-GG (dahil).")] string? from = null,
        [Description("Bitiş tarihi, YYYY-AA-GG (dahil).")] string? to = null,
        [Description("En fazla kaç satır (varsayılan 100, en çok 500).")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        McpAccess.Require(user, permissions, AppModules.Muhasebe, write: false);

        if (kind is not (null or Income or Expense))
        {
            throw new McpException("kind \"income\" ya da \"expense\" olmalı.");
        }

        var (fromDate, toExclusive) = ParseRange(from, to);
        var query = db.LedgerTransactions.AsNoTracking()
            .Where(x => !x.IsTransfer && x.IncomeExpenseCategory != null);
        if (categoryId.HasValue)
        {
            query = query.Where(x => x.IncomeExpenseCategoryId == categoryId.Value);
        }

        if (fromDate is { } f)
        {
            query = query.Where(x => x.Date >= f);
        }

        if (toExclusive is { } t)
        {
            query = query.Where(x => x.Date < t);
        }

        var entries = (await query
                .Include(x => x.IncomeExpenseCategory)
                .Include(x => x.CashBox)
                .Include(x => x.BankAccount)
                .ToListAsync(cancellationToken))
            .Select(x => (Entry: x, Kind: KindOf(x.IncomeExpenseCategory!)))
            .Where(x => kind is null || x.Kind == kind)
            .OrderByDescending(x => x.Entry.Date)
            .ThenByDescending(x => x.Entry.Id)
            .ToList();

        return new LedgerResult(
            entries.Sum(x => x.Entry.Amount),
            entries.Count,
            entries.Take(McpInput.ClampLimit(limit))
                .Select(x => new LedgerRow(
                    x.Entry.Id,
                    FormatDate(x.Entry.Date),
                    x.Kind,
                    x.Entry.IncomeExpenseCategory!.Name,
                    x.Entry.Amount,
                    x.Entry.CashBox?.Name ?? (x.Entry.BankAccount is null ? string.Empty : FinancialAccountHelper.BankName(x.Entry.BankAccount)),
                    x.Entry.Description,
                    links.Ledger(x.Entry.Id))).ToList());
    }

    private static string Key(CashBankListItemViewModel item) => $"{item.Type}:{item.Id}";

    private static string KindOf(IncomeExpenseCategory category)
        => CategoryTypeHelper.Normalize(category.Type) == CategoryTypeHelper.Gelir ? Income : Expense;

    private static (int? CashBoxId, int? BankAccountId) ParseAccountKey(string accountKey)
        => FinancialAccountHelper.TryParse(accountKey, out _, out var cashBoxId, out var bankAccountId)
            ? (cashBoxId, bankAccountId)
            : throw new McpException(BadAccountKey);

    // from/to dahil takvim günleri; kayıtlar UTC zaman damgası olduğundan bitiş, ertesi günün başlangıcıyla (hariç) sınırlanır.
    private static (DateTime? From, DateTime? ToExclusive) ParseRange(string? from, string? to)
    {
        var fromDate = from is null ? (DateTime?)null : McpInput.ParseDate(from);
        var toDate = to is null ? (DateTime?)null : McpInput.ParseDate(to);
        if (fromDate > toDate)
        {
            throw new McpException(BadRange);
        }

        return (fromDate, toDate?.AddDays(1));
    }

    private static string FormatDate(DateTime date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
