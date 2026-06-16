using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Kumburgaz.Web.Controllers;

[Authorize]
public class CashBankController(
    ApplicationDbContext db,
    CashBankDetailService detailService,
    ICollectionService collectionService) : Controller
{
    public async Task<IActionResult> Index(string? q = null)
    {
        var query = q?.Trim();

        var collections = await db.Collections
            .AsNoTracking()
            .Select(x => new { x.CashBoxId, x.BankAccountId, x.Amount })
            .ToListAsync();

        var ledgerRows = await db.LedgerTransactions
            .AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .Select(x => new
            {
                x.CashBoxId,
                x.BankAccountId,
                x.Amount,
                Type = x.IncomeExpenseCategory != null ? x.IncomeExpenseCategory.Type : CategoryTypeHelper.Gider
            })
            .ToListAsync();

        var cashBoxes = await db.CashBoxes
            .AsNoTracking()
            .Where(x => x.Active)
            .OrderBy(x => x.Name)
            .ToListAsync();

        var bankAccounts = await db.BankAccounts
            .AsNoTracking()
            .Where(x => x.Active)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Branch)
            .ToListAsync();

        var items = new List<CashBankListItemViewModel>();
        items.AddRange(cashBoxes.Select(x => new CashBankListItemViewModel
        {
            Id = x.Id,
            Type = "cash",
            Name = x.Name,
            Balance = x.OpeningBalance
                + collections.Where(c => c.CashBoxId == x.Id).Sum(c => c.Amount)
                + ledgerRows.Where(e => e.CashBoxId == x.Id).Sum(e => e.Type == CategoryTypeHelper.Gelir ? e.Amount : -e.Amount)
        }));
        items.AddRange(bankAccounts.Select(x => new CashBankListItemViewModel
        {
            Id = x.Id,
            Type = "bank",
            Name = string.IsNullOrWhiteSpace(x.Branch) ? x.Name : $"{x.Name} - {x.Branch}",
            Detail = x.Iban,
            Balance = x.OpeningBalance
                + collections.Where(c => c.BankAccountId == x.Id).Sum(c => c.Amount)
                + ledgerRows.Where(e => e.BankAccountId == x.Id).Sum(e => e.Type == CategoryTypeHelper.Gelir ? e.Amount : -e.Amount)
        }));

        if (!string.IsNullOrWhiteSpace(query))
        {
            items = items
                .Where(x => x.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || (x.Detail?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        return View(new CashBankIndexViewModel
        {
            Items = items.OrderByDescending(x => x.Type == "bank").ThenBy(x => x.Name).ToList(),
            Query = query ?? string.Empty
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCashBox(CashBoxFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ActionError"] = "Kasa bilgilerini kontrol edin.";
            return RedirectToAction(nameof(Index));
        }

        db.CashBoxes.Add(new CashBox
        {
            Name = model.Name,
            OpeningBalance = model.OpeningBalance,
            OpeningBalanceDate = DateTimeHelper.EnsureUtc(model.OpeningBalanceDate),
            Active = true
        });
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Kasa eklendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBankAccount(BankAccountFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ActionError"] = "Banka bilgilerini kontrol edin.";
            return RedirectToAction(nameof(Index));
        }

        db.BankAccounts.Add(new BankAccount
        {
            Name = model.Name,
            Branch = model.Branch,
            Iban = model.Iban,
            OpeningBalance = model.OpeningBalance,
            OpeningBalanceDate = DateTimeHelper.EnsureUtc(model.OpeningBalanceDate),
            Active = true
        });
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Banka kartı eklendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/CashBank/CashBox/{id:int}")]
    public async Task<IActionResult> CashBoxDetail(int id, CashBankDetailQuery query)
    {
        query.Type ??= "all"; query.Range ??= "all";
        if (Request.Query.ContainsKey("export") && Request.Query["export"] == "csv")
            return await ExportCsv("cash", id, query);
        var vm = await detailService.BuildAsync("cash", id, query);
        if (vm == null) return NotFound();
        ViewData["Title"] = vm.Name;
        return View("Detail", vm);
    }

    [HttpGet("/CashBank/Bank/{id:int}")]
    public async Task<IActionResult> BankDetail(int id, CashBankDetailQuery query)
    {
        query.Type ??= "all"; query.Range ??= "all";
        if (Request.Query.ContainsKey("export") && Request.Query["export"] == "csv")
            return await ExportCsv("bank", id, query);
        var vm = await detailService.BuildAsync("bank", id, query);
        if (vm == null) return NotFound();
        ViewData["Title"] = vm.Name;
        return View("Detail", vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAccount(CashBankAccountEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ActionError"] = "Hesap bilgilerini kontrol edin.";
            return RedirectToDetail(model.Kind, model.Id);
        }

        if (model.Kind == "bank")
        {
            var bank = await db.BankAccounts.FindAsync(model.Id);
            if (bank is null) return NotFound();
            bank.Name = model.Name;
            bank.Branch = model.Branch;
            bank.Iban = model.Iban;
        }
        else
        {
            var cash = await db.CashBoxes.FindAsync(model.Id);
            if (cash is null) return NotFound();
            cash.Name = model.Name;
        }

        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Hesap bilgileri güncellendi.";
        return RedirectToDetail(model.Kind, model.Id);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(string kind, int id, bool active)
    {
        if (kind == "bank")
        {
            var bank = await db.BankAccounts.FindAsync(id);
            if (bank is null) return NotFound();
            bank.Active = active;
        }
        else
        {
            var cash = await db.CashBoxes.FindAsync(id);
            if (cash is null) return NotFound();
            cash.Active = active;
        }

        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = active ? "Hesap aktifleştirildi." : "Hesap pasifleştirildi.";
        return RedirectToDetail(kind, id);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAccount(string kind, int id)
    {
        var hasCollections = await db.Collections.AnyAsync(x => kind == "bank" ? x.BankAccountId == id : x.CashBoxId == id);
        var hasLedger = await db.LedgerTransactions.AnyAsync(x => kind == "bank" ? x.BankAccountId == id : x.CashBoxId == id);
        if (hasCollections || hasLedger)
        {
            TempData["ActionError"] = "İşlem görmüş hesap silinemez. Pasifleştirebilirsiniz.";
            return RedirectToDetail(kind, id);
        }

        if (kind == "bank")
        {
            var bank = await db.BankAccounts.FindAsync(id);
            if (bank is null) return NotFound();
            db.BankAccounts.Remove(bank);
        }
        else
        {
            var cash = await db.CashBoxes.FindAsync(id);
            if (cash is null) return NotFound();
            db.CashBoxes.Remove(cash);
        }

        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Hesap silindi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateOpeningBalance(CashBankOpeningBalanceViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ActionError"] = "Açılış bakiyesi bilgilerini kontrol edin.";
            return RedirectToDetail(model.Kind, model.Id);
        }

        if (model.Kind == "bank")
        {
            var bank = await db.BankAccounts.FindAsync(model.Id);
            if (bank is null) return NotFound();
            bank.OpeningBalance = model.OpeningBalance;
            bank.OpeningBalanceDate = DateTimeHelper.EnsureUtc(model.OpeningBalanceDate);
        }
        else
        {
            var cash = await db.CashBoxes.FindAsync(model.Id);
            if (cash is null) return NotFound();
            cash.OpeningBalance = model.OpeningBalance;
            cash.OpeningBalanceDate = DateTimeHelper.EnsureUtc(model.OpeningBalanceDate);
        }

        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Açılış bakiyesi güncellendi.";
        return RedirectToDetail(model.Kind, model.Id);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteOpeningBalance(string kind, int id)
    {
        if (kind == "bank")
        {
            var bank = await db.BankAccounts.FindAsync(id);
            if (bank is null) return NotFound();
            bank.OpeningBalance = 0m;
        }
        else
        {
            var cash = await db.CashBoxes.FindAsync(id);
            if (cash is null) return NotFound();
            cash.OpeningBalance = 0m;
        }

        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Açılış bakiyesi silindi.";
        return RedirectToDetail(kind, id);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTransaction(string kind, int accountId, string source, int id)
    {
        if (source == "collection")
        {
            await collectionService.DeleteAsync(id);
        }
        else if (source == "ledger")
        {
            var ledger = await db.LedgerTransactions
                .Include(x => x.IncomeExpenseCategory)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (ledger is null) return NotFound();
            if (IsTransferLedger(ledger))
            {
                var candidates = await db.LedgerTransactions
                    .Include(x => x.IncomeExpenseCategory)
                    .Where(x => x.Id != ledger.Id)
                    .Where(x => x.Amount == ledger.Amount)
                    .Where(x => x.Date == ledger.Date)
                    .Where(x => x.Description == ledger.Description)
                    .ToListAsync();
                var pair = FindTransferPair(ledger, candidates);
                if (pair is not null)
                {
                    db.LedgerTransactions.Remove(pair);
                }
            }
            db.LedgerTransactions.Remove(ledger);
            await db.SaveChangesAsync();
        }
        else
        {
            return BadRequest();
        }

        TempData["ActionSuccess"] = "İşlem silindi.";
        return RedirectToDetail(kind, accountId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCollection(CashBankCollectionFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ActionError"] = "Tahsilat bilgilerini kontrol edin.";
            return RedirectToDetail(model.Kind, model.Id);
        }

        var accountKey = BuildAccountKey(model.Kind, model.Id);
        var paymentChannel = model.Kind == "bank" ? PaymentChannel.Bank : PaymentChannel.Cash;

        try
        {
            await collectionService.CreateAsync(new CollectionCreateViewModel
            {
                BillingGroupId = model.BillingGroupId,
                DuesInstallmentId = model.DuesInstallmentId,
                Date = model.Date,
                Amount = model.Amount,
                PaymentChannel = paymentChannel,
                AccountKey = accountKey,
                ReferenceNo = model.ReferenceNo,
                Note = model.Note
            });
            TempData["ActionSuccess"] = "Tahsilat kaydedildi.";
        }
        catch (Exception ex)
        {
            TempData["ActionError"] = ex.Message;
        }

        return RedirectToDetail(model.Kind, model.Id);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCollectionTransaction(int transactionId, CashBankCollectionFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ActionError"] = "Tahsilat bilgilerini kontrol edin.";
            return RedirectToDetail(model.Kind, model.Id);
        }

        try
        {
            await collectionService.UpdateAsync(transactionId, new CollectionCreateViewModel
            {
                BillingGroupId = model.BillingGroupId,
                DuesInstallmentId = model.DuesInstallmentId,
                Date = model.Date,
                Amount = model.Amount,
                PaymentChannel = model.Kind == "bank" ? PaymentChannel.Bank : PaymentChannel.Cash,
                AccountKey = BuildAccountKey(model.Kind, model.Id),
                ReferenceNo = model.ReferenceNo,
                Note = model.Note
            });
            TempData["ActionSuccess"] = "Tahsilat güncellendi.";
        }
        catch (Exception ex)
        {
            TempData["ActionError"] = ex.Message;
        }

        return RedirectToDetail(model.Kind, model.Id);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateLedger(CashBankLedgerFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ActionError"] = "Ödeme bilgilerini kontrol edin.";
            return RedirectToDetail(model.Kind, model.Id);
        }

        if (model.IsBankFee && model.Kind != "bank")
        {
            TempData["ActionError"] = "Banka masrafı yalnızca banka kartı için girilebilir.";
            return RedirectToDetail(model.Kind, model.Id);
        }

        var category = await db.IncomeExpenseCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == model.IncomeExpenseCategoryId && x.Type == CategoryTypeHelper.Gider);
        if (category is null)
        {
            TempData["ActionError"] = "Geçerli bir gider kategorisi seçin.";
            return RedirectToDetail(model.Kind, model.Id);
        }

        db.LedgerTransactions.Add(new LedgerTransaction
        {
            Date = DateTimeHelper.EnsureUtc(model.Date),
            IncomeExpenseCategoryId = model.IncomeExpenseCategoryId,
            Amount = model.Amount,
            PaymentChannel = model.Kind == "bank" ? PaymentChannel.Bank : PaymentChannel.Cash,
            CashBoxId = model.Kind == "cash" ? model.Id : null,
            BankAccountId = model.Kind == "bank" ? model.Id : null,
            Description = string.IsNullOrWhiteSpace(model.Description)
                ? (model.IsBankFee ? "Banka masrafı" : category.Name)
                : model.Description
        });

        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = model.IsBankFee ? "Banka masrafı kaydedildi." : "Ödeme kaydedildi.";
        return RedirectToDetail(model.Kind, model.Id);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateLedgerTransaction(int transactionId, CashBankLedgerFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ActionError"] = "Ödeme bilgilerini kontrol edin.";
            return RedirectToDetail(model.Kind, model.Id);
        }

        var entity = await db.LedgerTransactions.FindAsync(transactionId);
        if (entity is null) return NotFound();

        var category = await db.IncomeExpenseCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == model.IncomeExpenseCategoryId && x.Type == CategoryTypeHelper.Gider);
        if (category is null)
        {
            TempData["ActionError"] = "Geçerli bir gider kategorisi seçin.";
            return RedirectToDetail(model.Kind, model.Id);
        }

        entity.Date = DateTimeHelper.EnsureUtc(model.Date);
        entity.IncomeExpenseCategoryId = model.IncomeExpenseCategoryId;
        entity.Amount = model.Amount;
        entity.PaymentChannel = model.Kind == "bank" ? PaymentChannel.Bank : PaymentChannel.Cash;
        entity.CashBoxId = model.Kind == "cash" ? model.Id : null;
        entity.BankAccountId = model.Kind == "bank" ? model.Id : null;
        entity.Description = string.IsNullOrWhiteSpace(model.Description)
            ? (model.IsBankFee ? "Banka masrafı" : category.Name)
            : model.Description;

        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = model.IsBankFee ? "Banka masrafı güncellendi." : "Ödeme güncellendi.";
        return RedirectToDetail(model.Kind, model.Id);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTransfer(CashBankTransferFormViewModel model)
    {
        if (!ModelState.IsValid || !FinancialAccountHelper.TryParse(model.ToAccountKey, out var toChannel, out var toCashBoxId, out var toBankAccountId))
        {
            TempData["ActionError"] = "Transfer bilgilerini kontrol edin.";
            return RedirectToDetail(model.Kind, model.Id);
        }

        var fromKey = BuildAccountKey(model.Kind, model.Id);
        if (string.Equals(fromKey, model.ToAccountKey, StringComparison.OrdinalIgnoreCase))
        {
            TempData["ActionError"] = "Kaynak ve hedef hesap aynı olamaz.";
            return RedirectToDetail(model.Kind, model.Id);
        }

        var expenseCategoryId = await EnsureCategoryAsync("Para Transferi", CategoryTypeHelper.Gider);
        var incomeCategoryId = await EnsureCategoryAsync("Para Transferi", CategoryTypeHelper.Gelir);
        var descriptionPrefix = model.Kind == "cash" && toChannel == PaymentChannel.Bank ? "Bankaya yatır" : "Para transferi";
        var description = $"{descriptionPrefix}: {model.Description}".Trim().TrimEnd(':');
        var utcDate = DateTimeHelper.EnsureUtc(model.Date);

        db.LedgerTransactions.Add(new LedgerTransaction
        {
            Date = utcDate,
            IncomeExpenseCategoryId = expenseCategoryId,
            Amount = model.Amount,
            PaymentChannel = model.Kind == "bank" ? PaymentChannel.Bank : PaymentChannel.Cash,
            CashBoxId = model.Kind == "cash" ? model.Id : null,
            BankAccountId = model.Kind == "bank" ? model.Id : null,
            Description = description
        });
        db.LedgerTransactions.Add(new LedgerTransaction
        {
            Date = utcDate,
            IncomeExpenseCategoryId = incomeCategoryId,
            Amount = model.Amount,
            PaymentChannel = toChannel,
            CashBoxId = toCashBoxId,
            BankAccountId = toBankAccountId,
            Description = description
        });

        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Transfer kaydedildi.";
        return RedirectToDetail(model.Kind, model.Id);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateTransferTransaction(int transactionId, CashBankTransferFormViewModel model)
    {
        if (!ModelState.IsValid || !FinancialAccountHelper.TryParse(model.ToAccountKey, out var counterChannel, out var counterCashBoxId, out var counterBankAccountId))
        {
            TempData["ActionError"] = "Transfer bilgilerini kontrol edin.";
            return RedirectToDetail(model.Kind, model.Id);
        }

        var source = await db.LedgerTransactions
            .Include(x => x.IncomeExpenseCategory)
            .FirstOrDefaultAsync(x => x.Id == transactionId);
        if (source is null) return NotFound();

        var candidates = await db.LedgerTransactions
            .Include(x => x.IncomeExpenseCategory)
            .Where(x => x.Id != source.Id)
            .Where(x => x.Amount == source.Amount)
            .Where(x => x.Date == source.Date)
            .Where(x => x.Description == source.Description)
            .ToListAsync();
        var pair = FindTransferPair(source, candidates);
        if (pair is null)
        {
            TempData["ActionError"] = "Transferin karşı satırı bulunamadı.";
            return RedirectToDetail(model.Kind, model.Id);
        }

        var currentCashBoxId = model.Kind == "cash" ? model.Id : (int?)null;
        var currentBankAccountId = model.Kind == "bank" ? model.Id : (int?)null;
        var currentChannel = model.Kind == "bank" ? PaymentChannel.Bank : PaymentChannel.Cash;
        var sourceType = source.IncomeExpenseCategory?.Type ?? CategoryTypeHelper.Gider;
        var editingIncomingRow = sourceType == CategoryTypeHelper.Gelir;

        var fromCashBoxId = editingIncomingRow ? counterCashBoxId : currentCashBoxId;
        var fromBankAccountId = editingIncomingRow ? counterBankAccountId : currentBankAccountId;
        var fromChannel = editingIncomingRow ? counterChannel : currentChannel;
        var toCashBoxId = editingIncomingRow ? currentCashBoxId : counterCashBoxId;
        var toBankAccountId = editingIncomingRow ? currentBankAccountId : counterBankAccountId;
        var toChannel = editingIncomingRow ? currentChannel : counterChannel;

        if (fromCashBoxId == toCashBoxId && fromBankAccountId == toBankAccountId)
        {
            TempData["ActionError"] = "Kaynak ve karşı hesap aynı olamaz.";
            return RedirectToDetail(model.Kind, model.Id);
        }

        var expenseCategoryId = await EnsureCategoryAsync("Para Transferi", CategoryTypeHelper.Gider);
        var incomeCategoryId = await EnsureCategoryAsync("Para Transferi", CategoryTypeHelper.Gelir);
        var descriptionPrefix = fromCashBoxId.HasValue && toBankAccountId.HasValue ? "Bankaya yatır" : "Para transferi";
        var description = $"{descriptionPrefix}: {model.Description}".Trim().TrimEnd(':');
        var utcDate = DateTimeHelper.EnsureUtc(model.Date);

        var expenseRow = sourceType == CategoryTypeHelper.Gider ? source : pair;
        var incomeRow = sourceType == CategoryTypeHelper.Gelir ? source : pair;

        expenseRow.Date = utcDate;
        expenseRow.IncomeExpenseCategoryId = expenseCategoryId;
        expenseRow.Amount = model.Amount;
        expenseRow.PaymentChannel = fromChannel;
        expenseRow.CashBoxId = fromCashBoxId;
        expenseRow.BankAccountId = fromBankAccountId;
        expenseRow.Description = description;

        incomeRow.Date = utcDate;
        incomeRow.IncomeExpenseCategoryId = incomeCategoryId;
        incomeRow.Amount = model.Amount;
        incomeRow.PaymentChannel = toChannel;
        incomeRow.CashBoxId = toCashBoxId;
        incomeRow.BankAccountId = toBankAccountId;
        incomeRow.Description = description;

        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "Transfer güncellendi.";
        return RedirectToDetail(model.Kind, model.Id);
    }

    private async Task<IActionResult> ExportCsv(string kind, int id, CashBankDetailQuery query)
    {
        var vm = await detailService.BuildAsync(kind, id, query);
        if (vm == null) return NotFound();
        var rows = vm.Groups.SelectMany(g => g.Items).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("Tarih;Açıklama;Tip;Tutar;Bakiye");
        foreach (var r in rows)
            sb.AppendLine($"{r.Date:dd.MM.yyyy};{r.Description};{r.Kind};{r.Amount:N2};{r.RunningBalance:N2}");
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv;charset=utf-8", $"{vm.Name}-islemler.csv");
    }

    private RedirectToActionResult RedirectToDetail(string kind, int id)
    {
        return kind == "bank"
            ? RedirectToAction(nameof(BankDetail), new { id })
            : RedirectToAction(nameof(CashBoxDetail), new { id });
    }

    private static string BuildAccountKey(string kind, int id)
    {
        return kind == "bank" ? FinancialAccountHelper.BankKey(id) : FinancialAccountHelper.CashKey(id);
    }

    private async Task<int> EnsureCategoryAsync(string name, string type)
    {
        var category = await db.IncomeExpenseCategories
            .FirstOrDefaultAsync(x => x.Name == name && x.Type == type);
        if (category is not null)
        {
            return category.Id;
        }

        category = new IncomeExpenseCategory
        {
            Name = name,
            Type = type,
            Active = true
        };
        db.IncomeExpenseCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private static LedgerTransaction? FindTransferPair(LedgerTransaction source, List<LedgerTransaction> candidates)
    {
        var sourceType = source.IncomeExpenseCategory?.Type ?? CategoryTypeHelper.Gider;
        var targetType = sourceType == CategoryTypeHelper.Gelir ? CategoryTypeHelper.Gider : CategoryTypeHelper.Gelir;

        return candidates
            .Where(x => (x.IncomeExpenseCategory?.Type ?? CategoryTypeHelper.Gider) == targetType)
            .OrderBy(x => Math.Abs(x.Id - source.Id))
            .FirstOrDefault();
    }

    private static bool IsTransferLedger(LedgerTransaction tx)
    {
        var category = tx.IncomeExpenseCategory?.Name ?? string.Empty;
        var description = tx.Description ?? string.Empty;
        return category.Contains("Transfer", StringComparison.OrdinalIgnoreCase)
            || category.Contains("Para Transferi", StringComparison.OrdinalIgnoreCase)
            || description.StartsWith("Para transferi:", StringComparison.OrdinalIgnoreCase)
            || description.StartsWith("Bankaya yatır:", StringComparison.OrdinalIgnoreCase)
            || description.Equals("Para transferi", StringComparison.OrdinalIgnoreCase)
            || description.Equals("Bankaya yatır", StringComparison.OrdinalIgnoreCase);
    }
}
