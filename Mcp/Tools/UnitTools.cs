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

public sealed record SearchResult(string Type, int Id, string Title, string Subtitle, string Link);

public sealed record UnitPerson(string Name, string Type, string? Phone);

public sealed record OpenInstallment(int Id, string Period, string DueDate, decimal Amount, decimal Remaining);

public sealed record RecentCollection(int Id, string Date, decimal Amount, string Account);

public sealed record UnitDetail(
    int Id,
    string Display,
    string Block,
    List<UnitPerson> People,
    string? BillingGroup,
    decimal Balance,
    decimal DevirRemaining,
    List<OpenInstallment> OpenInstallments,
    List<RecentCollection> RecentCollections,
    string Link);

public sealed record StatementLine(string Date, string Kind, string Description, decimal Amount, decimal RunningBalance);

public sealed record UnitStatementResult(string Unit, List<StatementLine> Entries, decimal ClosingBalance);

public sealed record OverdueRow(int? UnitId, string Unit, string Responsible, decimal Remaining, string? OldestPeriod, string? Link);

public sealed record OverdueDuesResult(int Total, decimal TotalRemaining, List<OverdueRow> Rows);

[McpServerToolType]
public class UnitTools(
    ApplicationDbContext db,
    PermissionService permissions,
    UnitLedgerService unitLedger,
    UnitStatementService unitStatement,
    IReportingService reporting,
    McpLinks links)
{
    private const int MaxSearchResults = 20;
    private const int MaxRecentCollections = 5;
    private const int MinPhoneQueryLength = 4;
    private const string UnitNotFound = "Daire bulunamadı.";
    private const string DateFormat = "yyyy-MM-dd";

    private static readonly string[] UnitModules = [AppModules.Daireler, AppModules.Tahsilatlar];

    [McpServerTool(Name = "search", Title = "Daire ve hesap ara", ReadOnly = true)]
    [Description("Daire, malik/kiracı adı ve telefon (yetkiliyse) ile arar; en fazla 20 sonuç döner. "
        + "Büyük/küçük harf, Türkçe harf ve \"A Blok 5\" / \"A-5\" / \"A5\" farkı önemsizdir. "
        + "Sonuçlar: type \"unit\" ya da \"account\", id, title, subtitle, link. Hesap sonuçları yalnız Hesaplar yetkisiyle gelir.")]
    public async Task<List<SearchResult>> SearchAsync(
        ClaimsPrincipal user,
        [Description("Aranacak metin; en az 2 karakter (daire no, blok, ad, telefon).")] string query,
        CancellationToken cancellationToken = default)
    {
        McpAccess.RequireAny(user, permissions, UnitModules, write: false);

        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length < 2)
        {
            throw new McpException("Arama için en az 2 karakter girin.");
        }

        var key = TurkishSearchNormalizer.Normalize(trimmed);
        if (key.Length == 0)
        {
            return [];
        }

        var canSeePhone = CanSeePhone(user);
        var digitsOnly = key.All(char.IsDigit);
        // Kısa rakam sorgusu telefon ya da daire no içinde rastgele eşleşmesin.
        var phoneContains = digitsOnly && key.Length >= MinPhoneQueryLength;

        var units = await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .Include(x => x.UnitAccounts).ThenInclude(x => x.Account)
            .Include(x => x.CombinedUnitMembers).ThenInclude(x => x.ComponentUnit).ThenInclude(x => x!.Block)
            .ToListAsync(cancellationToken);

        var found = new List<(int Rank, int Order, string Sort, SearchResult Result)>();

        foreach (var unit in units)
        {
            var owner = AccountAssignmentService.ActiveOwner(unit);
            var tenant = AccountAssignmentService.ActiveTenant(unit);
            var ownerName = owner?.Name ?? unit.OwnerName;

            var ranks = new List<int?>
            {
                // Daire no: tam/önek eşleşir, rakam sorgusunda "içerir" gürültü yapar.
                Rank(key, TurkishSearchNormalizer.Normalize($"{unit.Block?.Name}{unit.UnitNo}"), allowContains: !digitsOnly),
                Rank(key, TurkishSearchNormalizer.Normalize(unit.UnitNo), allowContains: !digitsOnly),
                Rank(key, TurkishSearchNormalizer.Normalize(ownerName), allowContains: true),
                Rank(key, TurkishSearchNormalizer.Normalize(tenant?.Name), allowContains: true)
            };

            if (canSeePhone)
            {
                ranks.Add(Rank(key, TurkishSearchNormalizer.Normalize(unit.Phone), phoneContains));
                ranks.Add(Rank(key, TurkishSearchNormalizer.Normalize(owner?.Phone), phoneContains));
                ranks.Add(Rank(key, TurkishSearchNormalizer.Normalize(tenant?.Phone), phoneContains));
            }

            var best = ranks.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(-1).Min();
            if (best < 0)
            {
                continue;
            }

            var display = UnitDisplayHelper.Display(unit);
            found.Add((best, 0, $"{unit.Block?.Name}\u0001{unit.UnitNo.PadLeft(10)}",
                new SearchResult("unit", unit.Id, display, UnitSubtitle(ownerName, tenant?.Name), links.Unit(unit.Id))));
        }

        if (permissions.CanView(user, AppModules.Hesaplar))
        {
            var accounts = await db.Accounts.AsNoTracking().ToListAsync(cancellationToken);
            foreach (var account in accounts)
            {
                var best = new[]
                {
                    Rank(key, TurkishSearchNormalizer.Normalize(account.Name), allowContains: true),
                    Rank(key, TurkishSearchNormalizer.Normalize(account.Phone), phoneContains)
                }.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(-1).Min();
                if (best < 0)
                {
                    continue;
                }

                var subtitle = string.IsNullOrWhiteSpace(account.Phone)
                    ? AccountDisplayHelper.TypeLabel(account.AccountType)
                    : $"{AccountDisplayHelper.TypeLabel(account.AccountType)}, {account.Phone}";
                found.Add((best, 1, account.Name, new SearchResult("account", account.Id, account.Name, subtitle, links.Account(account.Id))));
            }
        }

        return found
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Order)
            .ThenBy(x => x.Sort, StringComparer.CurrentCulture)
            .Take(MaxSearchResults)
            .Select(x => x.Result)
            .ToList();
    }

    [McpServerTool(Name = "get_unit", Title = "Daire detayı", ReadOnly = true)]
    [Description("Bir dairenin malik/kiracısını, aidat grubunu, bakiyesini, devir borcunu, açık taksitlerini ve son 5 tahsilatını getirir. "
        + "balance: pozitif = daire borçlu, negatif = daire alacaklı (avans). devirRemaining: devir borcundan henüz ödenmemiş kısım. "
        + "Telefon yalnız Daireler ya da Hesaplar yetkisi olana gelir. unitId için önce search kullanın.")]
    public async Task<UnitDetail> GetUnitAsync(
        ClaimsPrincipal user,
        [Description("Dairenin id'si (search sonucundan).")] int unitId,
        CancellationToken cancellationToken = default)
    {
        McpAccess.RequireAny(user, permissions, UnitModules, write: false);

        var unit = await LoadUnitAsync(unitId, cancellationToken);
        var ledger = await unitLedger.BuildAsync(unitId) ?? throw new McpException(UnitNotFound);

        var canSeePhone = CanSeePhone(user);
        var owner = AccountAssignmentService.ActiveOwner(unit);
        var tenant = AccountAssignmentService.ActiveTenant(unit);
        var people = new List<UnitPerson>();
        var ownerName = owner?.Name ?? unit.OwnerName;
        if (!string.IsNullOrWhiteSpace(ownerName))
        {
            people.Add(new UnitPerson(ownerName, AccountDisplayHelper.TypeLabel(AccountType.Owner), PhoneFor(canSeePhone, owner?.Phone ?? unit.Phone)));
        }

        if (tenant is not null)
        {
            people.Add(new UnitPerson(tenant.Name, AccountDisplayHelper.TypeLabel(AccountType.Tenant), PhoneFor(canSeePhone, tenant.Phone)));
        }

        // Birim ekranındaki (UnitsController.Index) gibi: en son başlayan aidat grubu.
        var billingGroup = await db.BillingGroupUnits.AsNoTracking()
            .Where(x => x.UnitId == unitId)
            .OrderByDescending(x => x.StartPeriod)
            .ThenByDescending(x => x.Id)
            .Select(x => x.BillingGroup!.Name)
            .FirstOrDefaultAsync(cancellationToken);

        // UnitsController.BuildCollectionPeriodOptionsAsync ile aynı taksit kümesi.
        var installments = await db.DuesInstallments.AsNoTracking()
            .Where(x => (x.UnitId == unitId || (x.UnitId == null && x.BillingGroup!.Units.Any(u => u.UnitId == unitId)))
                        && x.RemainingAmount > 0)
            .ToListAsync(cancellationToken);
        var openInstallments = installments
            .OrderBy(x => PeriodHelper.ToKey(x.Period))
            .ThenBy(x => x.DueDate)
            .ThenBy(x => x.Id)
            .Select(x => new OpenInstallment(x.Id, x.Period, FormatDate(x.DueDate), x.Amount, x.RemainingAmount))
            .ToList();

        var recent = await RecentCollectionsAsync(ledger, cancellationToken);

        return new UnitDetail(
            unit.Id,
            UnitDisplayHelper.Display(unit),
            unit.Block?.Name ?? string.Empty,
            people,
            billingGroup,
            ledger.Summary.NetBalance,
            ledger.Summary.OpeningDebt,
            openInstallments,
            recent,
            links.Unit(unit.Id));
    }

    [McpServerTool(Name = "unit_statement", Title = "Daire ekstresi", ReadOnly = true)]
    [Description("Dairenin devir, tahakkuk ve tahsilat hareketlerini yürüyen bakiyeyle listeler; from/to ile tarih aralığına daraltılır. "
        + "amount: pozitif = borç artışı (tahakkuk, devir borcu), negatif = ödeme/alacak. runningBalance: o harekete kadarki toplam bakiye "
        + "(pozitif = borçlu), aralıktan bağımsızdır. closingBalance: seçilen bitiş tarihindeki (verilmezse güncel) bakiye. "
        + "kind: opening (devir), debt (tahakkuk), collection (tahsilat).")]
    public async Task<UnitStatementResult> UnitStatementAsync(
        ClaimsPrincipal user,
        [Description("Dairenin id'si (search sonucundan).")] int unitId,
        [Description("Başlangıç tarihi, YYYY-AA-GG (dahil). Verilmezse en baştan.")] string? from = null,
        [Description("Bitiş tarihi, YYYY-AA-GG (dahil). Verilmezse bugüne kadar.")] string? to = null,
        CancellationToken cancellationToken = default)
    {
        McpAccess.RequireAny(user, permissions, UnitModules, write: false);

        var fromDate = from is null ? (DateTime?)null : McpInput.ParseDate(from).Date;
        var toDate = to is null ? (DateTime?)null : McpInput.ParseDate(to).Date;
        if (fromDate > toDate)
        {
            throw new McpException("Başlangıç tarihi bitiş tarihinden sonra olamaz.");
        }

        var unit = await LoadUnitAsync(unitId, cancellationToken);
        var entries = await unitStatement.BuildAsync(unitId);

        var upToEnd = entries.Where(x => toDate is null || x.Date.Date <= toDate).ToList();
        var inRange = upToEnd.Where(x => fromDate is null || x.Date.Date >= fromDate).ToList();

        return new UnitStatementResult(
            UnitDisplayHelper.Display(unit),
            inRange.Select(x => new StatementLine(FormatDate(x.Date), KindName(x.Kind), x.Description, x.Amount, x.RunningBalance)).ToList(),
            upToEnd.Count > 0 ? upToEnd[^1].RunningBalance : 0m);
    }

    [McpServerTool(Name = "overdue_dues", Title = "Borçlu daireler", ReadOnly = true)]
    [Description("Kalan borcu sıfırdan büyük daireleri, en çok borçlu önce listeler (Daire Borç/Alacak raporuyla aynı hesap; devir borcu dahil). "
        + "remaining: toplam kalan borç; oldestPeriod: kalanı olan en eski dönem (yalnız devir borcu varsa \"Devir\"). "
        + "total: eşleşen toplam daire sayısı, totalRemaining: bunların toplam borcu (limit'ten bağımsız).")]
    public async Task<OverdueDuesResult> OverdueDuesAsync(
        ClaimsPrincipal user,
        [Description("Dönem, YYYY-YYYY (ör. 2026-2027). Verilmezse tüm dönemler.")] string? period = null,
        [Description("Yalnız bu blokta (blok id'si).")] int? blockId = null,
        [Description("En fazla kaç satır (varsayılan 100, en çok 500).")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        McpAccess.Require(user, permissions, AppModules.Raporlar, write: false);

        if (!string.IsNullOrWhiteSpace(period) && !PeriodHelper.IsValid(period.Trim()))
        {
            throw new McpException("Dönemi YYYY-YYYY biçiminde giriniz (ör. 2026-2027).");
        }

        var requestedPeriod = string.IsNullOrWhiteSpace(period) ? null : period.Trim();
        var rows = await reporting.GetDuesDebtReportAsync(new DuesDebtReportQuery
        {
            Period = requestedPeriod,
            BlockId = blockId,
            BalanceStatus = "debt"
        });

        var debtors = rows
            .Where(x => x.RemainingAmount > 0m)
            .OrderByDescending(x => x.RemainingAmount)
            .ThenBy(x => x.UnitDisplay, StringComparer.CurrentCulture)
            .ToList();

        var shown = debtors.Take(McpInput.ClampLimit(limit)).ToList();
        var oldest = await OldestOpenPeriodsAsync(shown, requestedPeriod, cancellationToken);

        return new OverdueDuesResult(
            debtors.Count,
            debtors.Sum(x => x.RemainingAmount),
            shown.Select(x => new OverdueRow(
                x.UnitId,
                x.UnitDisplay,
                x.ResponsibleAccountName,
                x.RemainingAmount,
                oldest.GetValueOrDefault(x) ?? (x.OpeningBalance < 0m ? "Devir" : null),
                x.UnitId.HasValue ? links.Unit(x.UnitId.Value) : null)).ToList());
    }

    private async Task<Unit> LoadUnitAsync(int unitId, CancellationToken cancellationToken)
    {
        // Silinmiş daireler EF sorgu filtresiyle zaten dışarıda kalır.
        return await db.Units.AsNoTracking()
            .Include(x => x.Block)
            .Include(x => x.UnitAccounts).ThenInclude(x => x.Account)
            .Include(x => x.CombinedUnitMembers).ThenInclude(x => x.ComponentUnit).ThenInclude(x => x!.Block)
            .FirstOrDefaultAsync(x => x.Id == unitId, cancellationToken)
            ?? throw new McpException(UnitNotFound);
    }

    // Son tahsilatlar ekstredeki (ledger) tahsilat satırlarından gelir; kasa/banka adı kayıttan okunur.
    private async Task<List<RecentCollection>> RecentCollectionsAsync(UnitLedgerResult ledger, CancellationToken cancellationToken)
    {
        var latest = ledger.Entries
            .Where(x => x.Kind == UnitLedgerEntryKind.Collection && x.SourceId.HasValue)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.SourceId)
            .Take(MaxRecentCollections)
            .ToList();
        var ids = latest.Select(x => x.SourceId!.Value).ToList();

        var collections = await db.Collections.AsNoTracking()
            .Include(x => x.CashBox)
            .Include(x => x.BankAccount)
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return latest.Select(entry =>
        {
            collections.TryGetValue(entry.SourceId!.Value, out var collection);
            var account = collection?.CashBox?.Name ?? collection?.BankAccount?.Name ?? string.Empty;
            return new RecentCollection(entry.SourceId.Value, FormatDate(entry.Date), -entry.Amount, account);
        }).ToList();
    }

    // Satır başına en eski kalanlı dönem. Birim satırı: kendi taksitleri + dairenin grubundaki ortak taksitler;
    // grup satırı (UnitId boş): grubun ortak taksitleri.
    private async Task<Dictionary<DuesDebtReportRow, string?>> OldestOpenPeriodsAsync(
        List<DuesDebtReportRow> rows, string? period, CancellationToken cancellationToken)
    {
        var result = new Dictionary<DuesDebtReportRow, string?>();
        if (rows.Count == 0)
        {
            return result;
        }

        var unitIds = rows.Where(x => x.UnitId.HasValue).Select(x => x.UnitId!.Value).ToHashSet();
        var groupIds = rows.Where(x => !x.UnitId.HasValue).Select(x => x.BillingGroupId).ToHashSet();

        var memberships = await db.BillingGroupUnits.AsNoTracking()
            .Where(x => unitIds.Contains(x.UnitId))
            .Select(x => new { x.UnitId, x.BillingGroupId })
            .ToListAsync(cancellationToken);
        var allGroupIds = memberships.Select(x => x.BillingGroupId).Concat(groupIds).ToHashSet();

        var open = await db.DuesInstallments.AsNoTracking()
            .Where(x => x.RemainingAmount > 0
                        && (period == null || x.Period == period)
                        && ((x.UnitId.HasValue && unitIds.Contains(x.UnitId.Value))
                            || (!x.UnitId.HasValue && allGroupIds.Contains(x.BillingGroupId))))
            .Select(x => new { x.UnitId, x.BillingGroupId, x.Period })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            var periods = row.UnitId is { } unitId
                ? open.Where(x => x.UnitId == unitId
                                  || (x.UnitId is null && memberships.Any(m => m.UnitId == unitId && m.BillingGroupId == x.BillingGroupId)))
                : open.Where(x => x.UnitId is null && x.BillingGroupId == row.BillingGroupId);
            result[row] = periods.Select(x => x.Period).OrderBy(PeriodHelper.ToKey).FirstOrDefault();
        }

        return result;
    }

    // Telefon: Daireler ya da Hesaplar görüntüleme yetkisi gerekir.
    private bool CanSeePhone(ClaimsPrincipal user)
        => permissions.CanView(user, AppModules.Daireler) || permissions.CanView(user, AppModules.Hesaplar);

    private static string? PhoneFor(bool allowed, string? phone)
        => allowed && !string.IsNullOrWhiteSpace(phone) ? phone : null;

    // 0 = tam eşleşme, 1 = önek, 2 = içerir; eşleşme yoksa null.
    private static int? Rank(string key, string candidate, bool allowContains)
    {
        if (candidate.Length == 0)
        {
            return null;
        }

        if (candidate == key)
        {
            return 0;
        }

        if (candidate.StartsWith(key, StringComparison.Ordinal))
        {
            return 1;
        }

        return allowContains && candidate.Contains(key, StringComparison.Ordinal) ? 2 : null;
    }

    private static string UnitSubtitle(string? ownerName, string? tenantName)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ownerName))
        {
            parts.Add($"Malik: {ownerName}");
        }

        if (!string.IsNullOrWhiteSpace(tenantName))
        {
            parts.Add($"Kiracı: {tenantName}");
        }

        return string.Join(", ", parts);
    }

    private static string KindName(StatementEntryKind kind) => kind switch
    {
        StatementEntryKind.OpeningBalance => "opening",
        StatementEntryKind.Debt => "debt",
        _ => "collection"
    };

    private static string FormatDate(DateTime date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
