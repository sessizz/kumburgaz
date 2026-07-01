using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public class CashBankDetailService(ApplicationDbContext db)
{
    public async Task<CashBankDetailViewModel?> BuildAsync(string kind, int id, CashBankDetailQuery q)
    {
        // 1. Hesabı bul
        string name; string? branch = null; string? iban = null;
        decimal openingBalance; DateTime openingDate; bool isActive;

        if (kind == "bank")
        {
            var bank = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (bank == null) return null;
            name = bank.Name; branch = bank.Branch; iban = bank.Iban;
            openingBalance = bank.OpeningBalance; openingDate = bank.OpeningBalanceDate; isActive = bank.Active;
        }
        else
        {
            var box = await db.CashBoxes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (box == null) return null;
            name = box.Name; openingBalance = box.OpeningBalance; openingDate = box.OpeningBalanceDate; isActive = box.Active;
        }

        // 2. Tüm işlemleri çek (uygulama tarafında birleştir)
        var collectionsRaw = await db.Collections
            .AsNoTracking()
            .Include(x => x.Unit).ThenInclude(u => u!.Block)
            .Include(x => x.BillingGroup)
            .Include(x => x.Allocations)
            .ThenInclude(x => x.DuesInstallment)
            .ThenInclude(x => x!.ResponsibleAccount)
            .Where(x => kind == "bank" ? x.BankAccountId == id : x.CashBoxId == id)
            .ToListAsync();

        var ledgerRaw = await db.LedgerTransactions
            .AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Where(x => kind == "bank" ? x.BankAccountId == id : x.CashBoxId == id)
            .ToListAsync();

        var allLedgerRaw = await db.LedgerTransactions
            .AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .ToListAsync();

        // 3. TxRow listesi oluştur
        var allRows = new List<TxRow>();

        foreach (var c in collectionsRaw)
        {
            var block = c.Unit?.Block?.Name ?? "";
            var unitNo = c.Unit?.UnitNo ?? "";
            var responsibleNames = c.Allocations
                .Select(x => x.DuesInstallment?.ResponsibleAccount?.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();
            var payer = responsibleNames.Count > 0
                ? string.Join(", ", responsibleNames)
                : c.Unit?.OwnerName ?? "";
            allRows.Add(new TxRow
            {
                Id = c.Id,
                Source = "collection",
                AccountKind = kind,
                AccountId = id,
                UnitId = c.UnitId,
                BillingGroupId = c.BillingGroupId,
                DuesInstallmentId = c.Allocations
                    .OrderBy(x => x.Id)
                    .Select(x => (int?)x.DuesInstallmentId)
                    .FirstOrDefault(),
                ReferenceNo = c.ReferenceNo,
                Note = c.Note,
                Description = c.BillingGroup?.Name ?? "Tahsilat",
                Subline = $"{block} Blok · No {unitNo} · {payer}",
                Kind = TxKind.Tahsilat,
                Amount = c.Amount,
                Date = c.Date
            });
        }

        foreach (var l in ledgerRaw)
        {
            var catType = l.IncomeExpenseCategory?.Type ?? CategoryTypeHelper.Gider;
            var kind2 = catType == "Gelir" ? TxKind.Girdi : TxKind.Cikis;
            var isTransfer = IsTransfer(l);
            allRows.Add(new TxRow
            {
                Id = l.Id,
                Source = "ledger",
                AccountKind = kind,
                AccountId = id,
                IncomeExpenseCategoryId = l.IncomeExpenseCategoryId,
                Description = l.Description ?? l.IncomeExpenseCategory?.Name ?? (isTransfer ? "Para transferi" : "Gider"),
                Subline = l.IncomeExpenseCategory?.Name,
                Kind = isTransfer ? TxKind.Transfer : kind2,
                Amount = SignedLedgerAmount(l),
                Date = l.Date
            });
        }

        // 4. Tarih sırala, açılış bakiyesini ilk satır olarak ekle, running balance hesapla
        allRows = allRows
            .OrderBy(r => r.Date)
            .ThenBy(r => r.Id)
            .ToList();

        if (openingBalance != 0m)
        {
            allRows.Insert(0, new TxRow
            {
                Id = 0,
                Source = "opening",
                AccountKind = kind,
                AccountId = id,
                Description = kind == "bank" ? "Banka açılış bakiyesi" : "Kasa açılış bakiyesi",
                Subline = "Devir",
                Kind = TxKind.Acilis,
                Amount = openingBalance,
                Date = openingDate
            });
        }

        decimal running = 0m;
        foreach (var r in allRows)
        {
            running += r.Amount;
            r.RunningBalance = running;
        }
        decimal balance = running;
        var lastTx = allRows.Any() ? allRows.Max(r => r.Date) : (DateTime?)null;

        // 5. Bu ay stat (açılış satırı hariç)
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthRows = allRows.Where(r => r.Kind != TxKind.Acilis && r.Date >= monthStart).ToList();
        var monthInflow = monthRows.Where(r => r.Amount > 0).Sum(r => r.Amount);
        var monthInflowCount = monthRows.Count(r => r.Amount > 0);
        var monthOutflow = monthRows.Where(r => r.Amount < 0).Sum(r => Math.Abs(r.Amount));
        var monthOutflowCount = monthRows.Count(r => r.Amount < 0);

        // 6. Son 14 gün activity (işlem sayısı, açılış hariç)
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var last14 = new int[14];
        for (int i = 0; i < 14; i++)
        {
            var day = today.AddDays(-(13 - i));
            last14[i] = allRows.Count(r => r.Kind != TxKind.Acilis && DateOnly.FromDateTime(r.Date) == day);
        }

        // 7. Filtrele
        var filtered = allRows.AsEnumerable();

        // Type filtre
        if (q.Type == "tahsilat") filtered = filtered.Where(r => r.Kind == TxKind.Tahsilat);
        else if (q.Type == "cikis") filtered = filtered.Where(r => r.Kind == TxKind.Cikis);
        else if (q.Type == "transfer") filtered = filtered.Where(r => r.Kind == TxKind.Transfer);

        // Range filtre
        if (q.Range == "this_month") filtered = filtered.Where(r => r.Date >= monthStart);
        else if (q.Range == "last_month")
        {
            var lmStart = monthStart.AddMonths(-1);
            filtered = filtered.Where(r => r.Date >= lmStart && r.Date < monthStart);
        }
        else if (q.Range == "last_90") filtered = filtered.Where(r => r.Date >= DateTime.UtcNow.AddDays(-90));
        else if (q.Range == "custom" && q.From.HasValue && q.To.HasValue)
        {
            var fromDt = q.From.Value.ToDateTime(TimeOnly.MinValue);
            var toDt = q.To.Value.ToDateTime(TimeOnly.MaxValue);
            filtered = filtered.Where(r => r.Date >= fromDt && r.Date <= toDt);
        }

        // Arama filtre
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var sq = q.Q.ToLowerInvariant();
            filtered = filtered.Where(r =>
                r.Description.ToLowerInvariant().Contains(sq) ||
                (r.Subline?.ToLowerInvariant().Contains(sq) ?? false));
        }

        var filteredList = filtered
            .OrderByDescending(r => r.Date)
            .ThenByDescending(r => r.Id)
            .ToList();
        var totalCount = filteredList.Count;
        var tahsilatCount = allRows.Count(r => r.Kind == TxKind.Tahsilat);
        var cikisCount = allRows.Count(r => r.Kind == TxKind.Cikis);
        var transferCount = allRows.Count(r => r.Kind == TxKind.Transfer);

        // 8. Sayfalama
        var page = Math.Max(1, q.Page);
        var paged = filteredList.Skip((page - 1) * q.PageSize).Take(q.PageSize).ToList();

        // 9. Gün grupları
        var groups = paged
            .GroupBy(r => DateOnly.FromDateTime(r.Date))
            .OrderByDescending(g => g.Key)
            .Select(g => new TxDayGroup
            {
                Date = g.Key,
                Net = g.Sum(r => r.Amount),
                Items = g.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToList()
            })
            .ToList();

        // 10. Audit history (basit: sadece hesap oluşturma)
        var history = new List<AuditEntry>
        {
            new() { Action = "created", Title = kind == "bank" ? "Banka eklendi" : "Kasa eklendi",
                    At = openingDate, ByUserName = "Sistem" }
        };

        var vm = new CashBankDetailViewModel
        {
            Kind = kind,
            Id = id,
            Name = name,
            Branch = branch,
            Iban = iban,
            IsActive = isActive,
            OpeningBalance = openingBalance,
            OpeningDate = DateOnly.FromDateTime(openingDate),
            Balance = balance,
            LastTransactionAt = lastTx,
            MonthInflow = monthInflow,
            MonthInflowCount = monthInflowCount,
            MonthOutflow = monthOutflow,
            MonthOutflowCount = monthOutflowCount,
            Last14DaysActivity = last14,
            Query = q,
            TotalCount = totalCount,
            TahsilatCount = tahsilatCount,
            CikisCount = cikisCount,
            TransferCount = transferCount,
            Groups = groups,
            History = history,
            PendingCount = 0
        };

        var currentAccountKey = kind == "bank"
            ? FinancialAccountHelper.BankKey(id)
            : FinancialAccountHelper.CashKey(id);
        var options = await BuildDetailOptionsAsync(currentAccountKey);
        vm.DuesOptions = options.DuesOptions;
        vm.IncomeCategoryOptions = options.IncomeCategories;
        vm.ExpenseCategoryOptions = options.ExpenseCategories;
        vm.TransferAccountOptions = options.TransferAccounts;
        ApplyRowOptions(vm, allLedgerRaw);

        return vm;
    }

    public async Task<List<TxRow>> GetAllRowsAsync(string kind, int id)
    {
        // Export için tüm satırlar
        var q = new CashBankDetailQuery { PageSize = int.MaxValue };
        var vm = await BuildAsync(kind, id, q);
        return vm?.Groups.SelectMany(g => g.Items).ToList() ?? new();
    }

    private static bool IsTransfer(LedgerTransaction tx)
    {
        if (tx.IsTransfer)
        {
            return true;
        }

        var category = tx.IncomeExpenseCategory?.Name ?? string.Empty;
        var description = tx.Description ?? string.Empty;
        return category.Contains("Transfer", StringComparison.OrdinalIgnoreCase)
            || category.Contains("Para Transferi", StringComparison.OrdinalIgnoreCase)
            || description.StartsWith("Para transferi:", StringComparison.OrdinalIgnoreCase)
            || description.StartsWith("Bankaya yatır:", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyRowOptions(CashBankDetailViewModel vm, List<LedgerTransaction> ledgerRaw)
    {
        foreach (var row in vm.Groups.SelectMany(x => x.Items))
        {
            row.DuesOptions = vm.DuesOptions;
            row.ExpenseCategoryOptions = vm.ExpenseCategoryOptions;
            row.TransferAccountOptions = vm.TransferAccountOptions;

            if (row.Source == "collection")
            {
                var selectedOption = vm.DuesOptions.FirstOrDefault(x => x.Id == row.DuesInstallmentId)
                    ?? vm.DuesOptions.FirstOrDefault(x => x.BillingGroupId == row.BillingGroupId && x.UnitId == row.UnitId)
                    ?? vm.DuesOptions.FirstOrDefault(x => x.BillingGroupId == row.BillingGroupId);
                row.DuesInstallmentId = selectedOption?.Id;
            }

            if (row.Kind != TxKind.Transfer || row.Source != "ledger")
            {
                continue;
            }

            var sourceLedger = ledgerRaw.FirstOrDefault(x => x.Id == row.Id);
            var pair = sourceLedger is null ? null : FindTransferPair(sourceLedger, ledgerRaw);
            row.ToAccountKey = pair is null
                ? null
                : FinancialAccountHelper.BuildKey(pair.CashBoxId, pair.BankAccountId);
        }
    }

    private static LedgerTransaction? FindTransferPair(LedgerTransaction source, List<LedgerTransaction> candidates)
    {
        var sourceIncoming = IsTransfer(source)
            ? source.TransferIsIncoming
            : source.IncomeExpenseCategory?.Type == CategoryTypeHelper.Gelir;

        return candidates
            .Where(x => x.Id != source.Id)
            .Where(x => x.Amount == source.Amount)
            .Where(x => x.Date == source.Date)
            .Where(x => string.Equals(x.Description, source.Description, StringComparison.Ordinal))
            .Where(IsTransfer)
            .Where(x => x.TransferIsIncoming != sourceIncoming)
            .OrderBy(x => Math.Abs(x.Id - source.Id))
            .FirstOrDefault();
    }

    private static decimal SignedLedgerAmount(LedgerTransaction tx)
    {
        if (IsTransfer(tx))
        {
            return tx.TransferIsIncoming ? tx.Amount : -tx.Amount;
        }

        return tx.IncomeExpenseCategory?.Type == CategoryTypeHelper.Gelir ? tx.Amount : -tx.Amount;
    }

    private async Task<DetailOptions> BuildDetailOptionsAsync(string currentAccountKey)
    {
        var installments = await db.DuesInstallments
            .AsNoTracking()
            .Include(x => x.BillingGroup)
            .ThenInclude(x => x!.DuesType)
            .Include(x => x.Unit)
            .ThenInclude(x => x!.Block)
            .Include(x => x.ResponsibleAccount)
            .Where(x => x.Unit == null || x.Unit.Active)
            .OrderBy(x => x.Period)
            .ThenBy(x => x.DueDate)
            .ThenBy(x => x.Unit!.Block!.Name)
            .ThenBy(x => x.Unit!.UnitNo)
            .ToListAsync();

        var expenseCategories = await db.IncomeExpenseCategories
            .AsNoTracking()
            .Where(x => x.Active && x.Type == CategoryTypeHelper.Gider)
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString()))
            .ToListAsync();

        var incomeCategories = await db.IncomeExpenseCategories
            .AsNoTracking()
            .Where(x => x.Active && x.Type == CategoryTypeHelper.Gelir)
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString()))
            .ToListAsync();

        var transferAccounts = (await FinancialAccountHelper.BuildOptionsAsync(db))
            .Where(x => x.Value != currentAccountKey)
            .ToList();

        return new DetailOptions(
            await BuildDuesOptionsAsync(installments),
            incomeCategories,
            expenseCategories,
            transferAccounts);
    }

    private async Task<List<CashBankDuesOptionViewModel>> BuildDuesOptionsAsync(List<DuesInstallment> installments)
    {
        var effectiveRemaining = installments.ToDictionary(x => x.Id, x => x.RemainingAmount);
        var units = await db.Units
            .AsNoTracking()
            .Where(x => x.Active && x.OpeningBalance > 0)
            .ToListAsync();

        foreach (var unit in units)
        {
            var credit = unit.OpeningBalance;
            foreach (var installment in installments
                         .Where(x => x.UnitId == unit.Id)
                         .OrderBy(x => x.AccrualDate)
                         .ThenBy(x => x.DueDate))
            {
                if (credit <= 0) break;
                var reduction = Math.Min(effectiveRemaining[installment.Id], credit);
                effectiveRemaining[installment.Id] -= reduction;
                credit -= reduction;
            }
        }

        return installments
            .GroupBy(x => new { x.BillingGroupId, x.UnitId })
            .Select(group =>
            {
                var ordered = group
                    .OrderByDescending(x => effectiveRemaining[x.Id] > 0)
                    .ThenBy(x => x.Period)
                    .ThenBy(x => x.DueDate)
                    .ThenBy(x => x.Id)
                    .ToList();
                var first = ordered.First();
                var unitText = first.Unit is not null ? UnitDisplayHelper.Display(first.Unit) : first.BillingGroup?.Name ?? "Aidat";
                var block = first.Unit?.Block?.Name ?? string.Empty;
                var unitNo = first.Unit?.UnitNo ?? string.Empty;
                var paddedUnitNo = int.TryParse(unitNo, out var unitNoNumber) ? unitNoNumber.ToString("00") : unitNo;
                var owner = first.Unit?.OwnerName ?? string.Empty;
                var responsible = first.ResponsibleAccount?.Name ?? string.Empty;
                var responsibleText = string.IsNullOrWhiteSpace(responsible) ? owner : responsible;
                var duesType = first.BillingGroup?.DuesType?.Name ?? "Aidat";
                var remaining = group.Sum(x => effectiveRemaining[x.Id]);
                var periods = string.Join(" ", group.Select(x => x.Period).Distinct());
                var text = $"{unitText} / {responsibleText} / {duesType} / Net kalan {remaining:N2} TL";
                return new CashBankDuesOptionViewModel
                {
                    Id = first.Id,
                    UnitId = first.UnitId,
                    BillingGroupId = first.BillingGroupId,
                    RemainingAmount = remaining,
                    Text = text,
                    SearchText = string.Join(" ", periods, block, unitNo, paddedUnitNo, $"{block}-{unitNo}", $"{block}-{paddedUnitNo}", unitText, owner, responsible, duesType, first.BillingGroup?.Name ?? string.Empty)
                };
            })
            .OrderByDescending(x => x.RemainingAmount > 0)
            .ThenBy(x => x.Text)
            .ToList();
    }

    private sealed record DetailOptions(
        List<CashBankDuesOptionViewModel> DuesOptions,
        List<SelectListItem> IncomeCategories,
        List<SelectListItem> ExpenseCategories,
        List<SelectListItem> TransferAccounts);
}
