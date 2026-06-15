using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
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
                Description = c.BillingGroup?.Name ?? "Tahsilat",
                Subline = $"{block} Blok · No {unitNo} · {payer}",
                Kind = TxKind.Tahsilat,
                Amount = c.Amount,
                Date = c.Date
            });
        }

        foreach (var l in ledgerRaw)
        {
            var catType = l.IncomeExpenseCategory?.Type ?? "Gider";
            var kind2 = catType == "Gelir" ? TxKind.Girdi : TxKind.Cikis;
            allRows.Add(new TxRow
            {
                Id = l.Id,
                Description = l.Description ?? l.IncomeExpenseCategory?.Name ?? "Gider",
                Subline = l.IncomeExpenseCategory?.Name,
                Kind = kind2,
                Amount = catType == "Gelir" ? l.Amount : -l.Amount,
                Date = l.Date
            });
        }

        // 4. Tarih sırala, açılış bakiyesini ilk satır olarak ekle, running balance hesapla
        allRows = allRows.OrderBy(r => r.Date).ThenBy(r => r.Id).ToList();

        if (openingBalance != 0m)
        {
            allRows.Insert(0, new TxRow
            {
                Id = 0,
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

        var filteredList = filtered.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToList();
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
                Items = g.OrderByDescending(r => r.Date).ToList()
            })
            .ToList();

        // 10. Audit history (basit: sadece hesap oluşturma)
        var history = new List<AuditEntry>
        {
            new() { Action = "created", Title = kind == "bank" ? "Banka eklendi" : "Kasa eklendi",
                    At = openingDate, ByUserName = "Sistem" }
        };

        return new CashBankDetailViewModel
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
    }

    public async Task<List<TxRow>> GetAllRowsAsync(string kind, int id)
    {
        // Export için tüm satırlar
        var q = new CashBankDetailQuery { PageSize = int.MaxValue };
        var vm = await BuildAsync(kind, id, q);
        return vm?.Groups.SelectMany(g => g.Items).ToList() ?? new();
    }
}
