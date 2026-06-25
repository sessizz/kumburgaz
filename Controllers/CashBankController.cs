using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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

    [HttpGet]
    public IActionResult DownloadImportTemplate(string kind, int id)
    {
        var rows = new[]
        {
            "tip;tarih;tutar;daire;kisi;kategori;aciklama;referans;not",
            "tahsilat;2026-06-16;2000,00;B-08;Alper Bahçeliler;;Haziran aidat tahsilatı;DK-001;",
            "gider;2026-06-16;1200,00;;;Bakım Onarım;Pompa bakım ödemesi;FIS-001;"
        };
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, rows)))
            .ToArray();
        return File(bytes, "text/csv;charset=utf-8", $"{kind}-{id}-import-sablonu.csv");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewImport(string kind, int id, IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["ActionError"] = "CSV dosyası seçin.";
            return RedirectToDetail(kind, id);
        }

        var rows = await CsvImportHelper.ReadRowsAsync(file);
        if (rows.Count < 2)
        {
            TempData["ActionError"] = "CSV başlık ve en az bir veri satırı içermelidir.";
            return RedirectToDetail(kind, id);
        }

        var detail = await detailService.BuildAsync(kind, id, new CashBankDetailQuery());
        if (detail is null) return NotFound();

        var headers = BuildImportHeaders(rows[0]);
        var previewRows = rows.Skip(1)
            .Select((row, index) => BuildImportPreviewRow(row, headers, index + 2, detail))
            .ToList();

        return View("ImportPreview", new CashBankImportPreviewViewModel
        {
            Kind = kind,
            Id = id,
            AccountName = detail.Branch is null ? detail.Name : $"{detail.Name} · {detail.Branch}",
            Rows = previewRows,
            DuesOptions = detail.DuesOptions,
            ExpenseCategoryOptions = detail.ExpenseCategoryOptions
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CommitImport(CashBankImportPreviewViewModel model)
    {
        var detail = await detailService.BuildAsync(model.Kind, model.Id, new CashBankDetailQuery());
        if (detail is null) return NotFound();

        var accountKey = BuildAccountKey(model.Kind, model.Id);
        var paymentChannel = model.Kind == "bank" ? PaymentChannel.Bank : PaymentChannel.Cash;
        var errors = new List<string>();
        var importRows = new List<CashBankImportOperation>();

        foreach (var row in model.Rows.Where(x => x.Include))
        {
            if (!TryParseDate(row.Date, out var date))
            {
                errors.Add($"{row.LineNo}. satır: tarih geçersiz.");
                continue;
            }

            if (!TryParseImportAmount(row.Amount, out var amount))
            {
                errors.Add($"{row.LineNo}. satır: tutar geçersiz.");
                continue;
            }

            if (row.Type == "collection")
            {
                if (!row.DuesInstallmentId.HasValue)
                {
                    errors.Add($"{row.LineNo}. satır: tahsilat için aidat borcu seçin.");
                    continue;
                }

                var installment = await db.DuesInstallments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == row.DuesInstallmentId.Value);
                if (installment is null)
                {
                    errors.Add($"{row.LineNo}. satır: aidat borcu bulunamadı.");
                    continue;
                }

                importRows.Add(new CashBankImportOperation(row, date, amount, installment, null));
                continue;
            }

            if (!row.ExpenseCategoryId.HasValue)
            {
                errors.Add($"{row.LineNo}. satır: gider kategorisi seçin.");
                continue;
            }

            var categoryExists = await db.IncomeExpenseCategories
                .AsNoTracking()
                .AnyAsync(x => x.Id == row.ExpenseCategoryId.Value && x.Type == CategoryTypeHelper.Gider);
            if (!categoryExists)
            {
                errors.Add($"{row.LineNo}. satır: gider kategorisi bulunamadı.");
                continue;
            }

            importRows.Add(new CashBankImportOperation(row, date, amount, null, row.ExpenseCategoryId.Value));
        }

        if (errors.Count > 0)
        {
            return ImportPreviewWithErrors(model, detail, errors, "CSV import edilmedi. Hatalı satırları düzeltin.");
        }

        importRows = ApplyImportOrder(importRows, await BuildExistingTransactionDateOffsetsAsync(model.Kind, model.Id));
        var saved = importRows.Count;
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var item in importRows)
            {
                if (item.Installment is not null)
                {
                    await collectionService.CreateAsync(new CollectionCreateViewModel
                    {
                        BillingGroupId = item.Installment.BillingGroupId,
                        DuesInstallmentId = item.Installment.Id,
                        Date = item.Date,
                        Amount = item.Amount,
                        PaymentChannel = paymentChannel,
                        AccountKey = accountKey,
                        ReferenceNo = item.Row.ReferenceNo,
                        Note = string.IsNullOrWhiteSpace(item.Row.Note) ? item.Row.Description : item.Row.Note
                    });
                    continue;
                }

                db.LedgerTransactions.Add(new LedgerTransaction
                {
                    Date = DateTimeHelper.EnsureUtc(item.Date),
                    IncomeExpenseCategoryId = item.ExpenseCategoryId!.Value,
                    Amount = item.Amount,
                    PaymentChannel = paymentChannel,
                    CashBoxId = model.Kind == "cash" ? model.Id : null,
                    BankAccountId = model.Kind == "bank" ? model.Id : null,
                    Description = string.IsNullOrWhiteSpace(item.Row.Description) ? item.Row.Note : item.Row.Description
                });
                await db.SaveChangesAsync();
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return ImportPreviewWithErrors(model, detail, [$"Import kaydedilemedi: {ex.Message}"], "CSV import edilmedi.");
        }

        TempData["ActionSuccess"] = $"{saved} kayıt import edildi.";
        return RedirectToDetail(model.Kind, model.Id);
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
    public async Task<IActionResult> SetActive(string kind, int id, bool? active, string? command = null)
    {
        var shouldBeActive = command?.Equals("activate", StringComparison.OrdinalIgnoreCase) == true
            ? true
            : command?.Equals("deactivate", StringComparison.OrdinalIgnoreCase) == true
                ? false
                : active ?? false;

        if (kind == "bank")
        {
            var bank = await db.BankAccounts.FindAsync(id);
            if (bank is null) return NotFound();
            bank.Active = shouldBeActive;
        }
        else
        {
            var cash = await db.CashBoxes.FindAsync(id);
            if (cash is null) return NotFound();
            cash.Active = shouldBeActive;
        }

        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = shouldBeActive ? "Hesap aktifleştirildi." : "Hesap pasifleştirildi.";
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
        ModelState.Remove(nameof(model.OpeningBalance));
        if (!TryReadFormDecimal(nameof(model.OpeningBalance), out var openingBalance, _ => true))
        {
            ModelState.AddModelError(nameof(model.OpeningBalance), "Geçerli bir tutar giriniz.");
        }
        else
        {
            model.OpeningBalance = openingBalance;
        }

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
            if (!await CollectionBelongsToAccountAsync(id, kind, accountId)) return NotFound();
            await collectionService.DeleteAsync(id);
        }
        else if (source == "ledger")
        {
            if (!await DeleteLedgerTransactionAsync(id, kind, accountId)) return NotFound();
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
    public async Task<IActionResult> DeleteSelectedTransactions(string kind, int accountId, List<string>? selectedTransactions)
    {
        var selections = (selectedTransactions ?? [])
            .Select(ParseSelectedTransaction)
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        if (selections.Count == 0)
        {
            TempData["ActionError"] = "Silmek icin en az bir islem secin.";
            return RedirectToDetail(kind, accountId);
        }

        var deleted = 0;
        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var (selectedSource, selectedId) in selections)
        {
            if (selectedSource == "collection")
            {
                if (!await CollectionBelongsToAccountAsync(selectedId, kind, accountId))
                {
                    continue;
                }

                await collectionService.DeleteAsync(selectedId);
                deleted++;
                continue;
            }

            if (selectedSource == "ledger")
            {
                deleted += await DeleteLedgerTransactionAsync(selectedId, kind, accountId) ? 1 : 0;
            }
        }

        await transaction.CommitAsync();

        TempData[deleted > 0 ? "ActionSuccess" : "ActionError"] = deleted > 0
            ? $"{deleted} islem silindi."
            : "Secilen islemler bulunamadi.";
        return RedirectToDetail(kind, accountId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCollection(CashBankCollectionFormViewModel model)
    {
        if (!TryReadAmount(out var parsedAmount))
        {
            ModelState.AddModelError(nameof(model.Amount), "Geçerli bir tutar giriniz.");
        }
        else
        {
            model.Amount = parsedAmount;
        }

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
        if (!TryReadAmount(out var parsedAmount))
        {
            ModelState.AddModelError(nameof(model.Amount), "Geçerli bir tutar giriniz.");
        }
        else
        {
            model.Amount = parsedAmount;
        }

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
        if (!TryReadAmount(out var parsedAmount))
        {
            ModelState.AddModelError(nameof(model.Amount), "Geçerli bir tutar giriniz.");
        }
        else
        {
            model.Amount = parsedAmount;
        }

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
        if (!TryReadAmount(out var parsedAmount))
        {
            ModelState.AddModelError(nameof(model.Amount), "Geçerli bir tutar giriniz.");
        }
        else
        {
            model.Amount = parsedAmount;
        }

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
        if (!TryReadAmount(out var parsedAmount))
        {
            ModelState.AddModelError(nameof(model.Amount), "Geçerli bir tutar giriniz.");
        }
        else
        {
            model.Amount = parsedAmount;
        }

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
        if (!TryReadAmount(out var parsedAmount))
        {
            ModelState.AddModelError(nameof(model.Amount), "Geçerli bir tutar giriniz.");
        }
        else
        {
            model.Amount = parsedAmount;
        }

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

    private static (string Source, int Id)? ParseSelectedTransaction(string value)
    {
        var parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var id))
        {
            return null;
        }

        return parts[0] is "collection" or "ledger" ? (parts[0], id) : null;
    }

    private Task<bool> CollectionBelongsToAccountAsync(int id, string kind, int accountId)
    {
        return db.Collections.AnyAsync(x =>
            x.Id == id && (kind == "bank" ? x.BankAccountId == accountId : x.CashBoxId == accountId));
    }

    private async Task<bool> DeleteLedgerTransactionAsync(int id, string kind, int accountId)
    {
        var ledger = await db.LedgerTransactions
            .Include(x => x.IncomeExpenseCategory)
            .FirstOrDefaultAsync(x =>
                x.Id == id && (kind == "bank" ? x.BankAccountId == accountId : x.CashBoxId == accountId));
        if (ledger is null)
        {
            return false;
        }

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
        return true;
    }

    private bool TryReadAmount(out decimal amount)
    {
        return TryReadFormDecimal("Amount", out amount, value => value > 0);
    }

    private bool TryReadFormDecimal(string fieldName, out decimal amount, Func<decimal, bool> isValid)
    {
        ModelState.Remove(fieldName);
        amount = 0m;
        var raw = Request.Form[fieldName].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (!FlexibleDecimalParser.TryParse(raw, out amount))
        {
            return false;
        }

        return isValid(amount);
    }

    private static CashBankImportRowViewModel BuildImportPreviewRow(
        string[] row,
        Dictionary<string, int> headers,
        int lineNo,
        CashBankDetailViewModel detail)
    {
        var typeText = ReadImportValue(row, headers, "tip", "type", "tur", "islemtipi", "işlemtipi");
        var amountText = ReadImportValue(row, headers, "tutar", "amount", "borc", "borç", "alacak");
        var description = ReadImportValue(row, headers, "aciklama", "açıklama", "description", "izahat", "detay");
        var categoryText = ReadImportValue(row, headers, "kategori", "category", "giderkategori", "giderkategorisi");
        var matchText = ReadImportValue(row, headers, "daire", "hesap", "kisi", "kişi", "isim", "ad", "malik", "kiraci", "kiracı", "uye", "üye");
        var note = ReadImportValue(row, headers, "not", "note");
        var reference = ReadImportValue(row, headers, "referans", "referansno", "ref", "fisno", "fişno", "dekont");
        var dateText = ReadImportValue(row, headers, "tarih", "date", "islemtarihi", "işlemtarihi");
        var rowType = InferImportType(typeText, amountText, description, categoryText);
        var status = string.Empty;

        var preview = new CashBankImportRowViewModel
        {
            LineNo = lineNo,
            Type = rowType,
            Date = TryParseDate(dateText, out var parsedDate) ? parsedDate.ToString("yyyy-MM-dd") : dateText,
            Amount = NormalizeImportAmountText(amountText),
            Description = string.IsNullOrWhiteSpace(description) ? categoryText : description,
            ReferenceNo = reference,
            Note = note
        };

        if (rowType == "collection")
        {
            var match = MatchDuesOption(detail.DuesOptions, matchText, string.Join(" ", description, note, reference));
            preview.DuesInstallmentId = match?.Id;
            if (match is not null && string.IsNullOrWhiteSpace(preview.Amount))
            {
                preview.Amount = match.RemainingAmount.ToString("0.##", CultureInfo.GetCultureInfo("tr-TR"));
            }
            if (match is null)
            {
                status = "Aidat borcu eşleşmedi.";
            }
        }
        else
        {
            var category = MatchCategory(detail.ExpenseCategoryOptions, string.Join(" ", categoryText, description, note));
            preview.ExpenseCategoryId = category;
            if (category is null)
            {
                status = "Gider kategorisi eşleşmedi.";
            }
        }

        if (string.IsNullOrWhiteSpace(preview.Date) || !TryParseDate(preview.Date, out _))
        {
            status = AppendImportStatus(status, "Tarih kontrol edilmeli.");
        }
        if (string.IsNullOrWhiteSpace(preview.Amount) || !TryParseImportAmount(preview.Amount, out _))
        {
            status = AppendImportStatus(status, "Tutar kontrol edilmeli.");
        }

        preview.Status = status;
        return preview;
    }

    private IActionResult ImportPreviewWithErrors(
        CashBankImportPreviewViewModel model,
        CashBankDetailViewModel detail,
        List<string> errors,
        string message)
    {
        model.DuesOptions = detail.DuesOptions;
        model.ExpenseCategoryOptions = detail.ExpenseCategoryOptions;
        model.AccountName = detail.Branch is null ? detail.Name : $"{detail.Name} · {detail.Branch}";
        var rowErrors = errors
            .Where(x => x.Contains(". satır:", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var generalErrors = errors
            .Except(rowErrors)
            .Distinct()
            .ToList();

        foreach (var row in model.Rows)
        {
            row.Status = rowErrors.FirstOrDefault(x => x.StartsWith($"{row.LineNo}. satır:", StringComparison.OrdinalIgnoreCase)) ?? row.Status;
        }

        var parts = new List<string>();
        if (rowErrors.Count > 0)
        {
            parts.Add($"{rowErrors.Count} satır kontrol istiyor.");
        }
        if (generalErrors.Count > 0)
        {
            parts.Add(string.Join(" ", generalErrors));
        }

        var suffix = parts.Count > 0 ? string.Join(" ", parts) : $"{errors.Count} hata var.";
        TempData["ActionError"] = $"{message} {suffix}";
        return View("ImportPreview", model);
    }

    private async Task<Dictionary<DateTime, int>> BuildExistingTransactionDateOffsetsAsync(string kind, int id)
    {
        var collectionQuery = db.Collections.AsNoTracking();
        var ledgerQuery = db.LedgerTransactions.AsNoTracking();

        if (kind == "cash")
        {
            collectionQuery = collectionQuery.Where(x => x.CashBoxId == id);
            ledgerQuery = ledgerQuery.Where(x => x.CashBoxId == id);
        }
        else
        {
            collectionQuery = collectionQuery.Where(x => x.BankAccountId == id);
            ledgerQuery = ledgerQuery.Where(x => x.BankAccountId == id);
        }

        var collectionDates = await collectionQuery.Select(x => x.Date).ToListAsync();
        var ledgerDates = await ledgerQuery.Select(x => x.Date).ToListAsync();

        return collectionDates
            .Concat(ledgerDates)
            .GroupBy(x => x.Date)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static List<CashBankImportOperation> ApplyImportOrder(
        List<CashBankImportOperation> rows,
        Dictionary<DateTime, int> existingOffsets)
    {
        var offsets = new Dictionary<DateTime, int>(existingOffsets);
        var orderedRows = new List<CashBankImportOperation>(rows.Count);

        foreach (var row in rows)
        {
            var day = row.Date.Date;
            offsets.TryGetValue(day, out var nextOffset);
            nextOffset++;
            offsets[day] = nextOffset;
            orderedRows.Add(row with { Date = day.AddTicks(nextOffset) });
        }

        return orderedRows;
    }

    private sealed record CashBankImportOperation(
        CashBankImportRowViewModel Row,
        DateTime Date,
        decimal Amount,
        DuesInstallment? Installment,
        int? ExpenseCategoryId);

    private static Dictionary<string, int> BuildImportHeaders(string[] row)
    {
        var map = new Dictionary<string, int>();
        for (var i = 0; i < row.Length; i++)
        {
            var key = NormalizeImportKey(row[i]);
            if (!string.IsNullOrWhiteSpace(key))
            {
                map[key] = i;
            }
        }

        return map;
    }

    private static string ReadImportValue(string[] row, Dictionary<string, int> headers, params string[] keys)
    {
        foreach (var key in keys.Select(NormalizeImportKey))
        {
            if (headers.TryGetValue(key, out var idx) && idx < row.Length && !string.IsNullOrWhiteSpace(row[idx]))
            {
                return row[idx].Trim();
            }
        }

        return string.Empty;
    }

    private static string NormalizeImportKey(string value)
    {
        return value.Trim()
            .ToLowerInvariant()
            .Replace("ı", "i")
            .Replace("ğ", "g")
            .Replace("ü", "u")
            .Replace("ş", "s")
            .Replace("ö", "o")
            .Replace("ç", "c")
            .Replace(" ", "")
            .Replace("_", "")
            .Replace("-", "");
    }

    private static string InferImportType(string typeText, string amountText, string description, string categoryText)
    {
        var haystack = NormalizeImportKey(string.Join(" ", typeText, description, categoryText));
        if (haystack.Contains("tahsil") || haystack.Contains("aidat") || haystack.Contains("gelir"))
        {
            return "collection";
        }
        if (haystack.Contains("gider") || haystack.Contains("odeme") || haystack.Contains("masraf"))
        {
            return "expense";
        }
        return amountText.TrimStart().StartsWith("-", StringComparison.Ordinal) ? "expense" : "collection";
    }

    private static CashBankDuesOptionViewModel? MatchDuesOption(List<CashBankDuesOptionViewModel> options, string primaryText, string secondaryText)
    {
        var unitCodes = ExtractUnitCodeCandidates(primaryText);
        if (unitCodes.Count > 0)
        {
            var exactUnitMatches = options
                .Where(x =>
                {
                    var haystack = NormalizeSearchText($"{x.SearchText} {x.Text}");
                    return unitCodes.Any(code => haystack.Contains(code, StringComparison.OrdinalIgnoreCase));
                })
                .ToList();

            if (exactUnitMatches.Count == 0)
            {
                return null;
            }

            options = exactUnitMatches;
        }

        var tokens = TokenizeImportSearch(string.Join(" ", primaryText, secondaryText));
        if (tokens.Count == 0)
        {
            return null;
        }

        return options
            .Select(x => new
            {
                Option = x,
                Haystack = NormalizeSearchText($"{x.SearchText} {x.Text}")
            })
            .Select(x => new
            {
                x.Option,
                Score = tokens.Count(token => x.Haystack.Contains(token, StringComparison.OrdinalIgnoreCase))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Option.RemainingAmount > 0)
            .ThenBy(x => x.Option.Text.Length)
            .Select(x => x.Option)
            .FirstOrDefault();
    }

    private static List<string> ExtractUnitCodeCandidates(string value)
    {
        return Regex.Matches(value, @"(?i)\b[\p{L}]{1,4}\s*[-/]?\s*\d+[a-zA-Z]?\b")
            .Select(x => NormalizeSearchText(x.Value))
            .Where(x => x.Any(char.IsLetter) && x.Any(char.IsDigit))
            .Distinct()
            .ToList();
    }

    private static int? MatchCategory(List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> options, string text)
    {
        var tokens = TokenizeImportSearch(text);
        if (tokens.Count == 0)
        {
            return null;
        }

        var match = options
            .Select(x => new
            {
                Option = x,
                Haystack = NormalizeSearchText(x.Text)
            })
            .Select(x => new
            {
                x.Option,
                Score = tokens.Count(token => x.Haystack.Contains(token, StringComparison.OrdinalIgnoreCase))
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Option.Text.Length)
            .Select(x => x.Option)
            .FirstOrDefault();

        return int.TryParse(match?.Value, out var id) ? id : null;
    }

    private static string NormalizeSearchText(string value)
    {
        return value.Trim()
            .ToLowerInvariant()
            .Replace("ı", "i")
            .Replace("ğ", "g")
            .Replace("ü", "u")
            .Replace("ş", "s")
            .Replace("ö", "o")
            .Replace("ç", "c")
            .Replace(".", "")
            .Replace("-", "")
            .Replace("_", "")
            .Replace("/", "")
            .Replace("\\", "")
            .Replace(" ", "");
    }

    private static List<string> TokenizeImportSearch(string value)
    {
        var cleaned = value.Trim()
            .ToLowerInvariant()
            .Replace("ı", "i")
            .Replace("ğ", "g")
            .Replace("ü", "u")
            .Replace("ş", "s")
            .Replace("ö", "o")
            .Replace("ç", "c");

        foreach (var separator in new[] { '.', ',', ';', ':', '-', '_', '/', '\\', '|', '(', ')', '[', ']' })
        {
            cleaned = cleaned.Replace(separator, ' ');
        }

        return cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 1)
            .Distinct()
            .ToList();
    }

    private static string NormalizeImportAmountText(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("-", StringComparison.Ordinal) ? trimmed[1..].Trim() : trimmed;
    }

    private static string AppendImportStatus(string current, string message)
    {
        return string.IsNullOrWhiteSpace(current) ? message : $"{current} {message}";
    }

    private static bool TryParseDate(string value, out DateTime date)
    {
        var formats = new[] { "yyyy-MM-dd", "dd.MM.yyyy", "dd/MM/yyyy", "MM/dd/yyyy" };
        return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateTime.TryParse(value, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out date)
            || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryParseImportAmount(string value, out decimal amount)
    {
        amount = 0m;
        var raw = NormalizeImportAmountText(value);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return FlexibleDecimalParser.TryParse(raw, out amount) && amount > 0;
    }
}
