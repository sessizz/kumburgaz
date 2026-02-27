using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Controllers;

[Authorize]
public class LedgerController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var rows = await db.LedgerTransactions.AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .ToListAsync();
        return View(rows);
    }

    public async Task<IActionResult> ExportCsv()
    {
        var rows = await db.LedgerTransactions.AsNoTracking()
            .Include(x => x.IncomeExpenseCategory)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        var csvRows = new List<string[]>
        {
            new[] { "IncomeExpenseCategoryId", "CategoryType", "CategoryName", "Date", "Amount", "PaymentChannel", "Description" }
        };

        csvRows.AddRange(rows.Select(x => new[]
        {
            x.IncomeExpenseCategoryId.ToString(),
            x.IncomeExpenseCategory?.Type ?? string.Empty,
            x.IncomeExpenseCategory?.Name ?? string.Empty,
            x.Date.ToString("yyyy-MM-dd"),
            x.Amount.ToString(CultureInfo.InvariantCulture),
            x.PaymentChannel.ToString(),
            x.Description ?? string.Empty
        }));

        var bytes = CsvExportHelper.BuildCsv(csvRows.ToArray());
        return File(bytes, "text/csv; charset=utf-8", "gelir-gider.csv");
    }

    public async Task<IActionResult> Create()
    {
        return View(await BuildAsync(new LedgerTransactionCreateViewModel()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LedgerTransactionCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(await BuildAsync(model));
        }

        db.LedgerTransactions.Add(new LedgerTransaction
        {
            Date = DateTimeHelper.EnsureUtc(model.Date),
            IncomeExpenseCategoryId = model.IncomeExpenseCategoryId,
            Amount = model.Amount,
            PaymentChannel = model.PaymentChannel,
            Description = model.Description
        });

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var entity = await db.LedgerTransactions.FindAsync(id);
        if (entity is null)
        {
            return NotFound();
        }

        var model = new LedgerTransactionCreateViewModel
        {
            Date = entity.Date,
            IncomeExpenseCategoryId = entity.IncomeExpenseCategoryId,
            Amount = entity.Amount,
            PaymentChannel = entity.PaymentChannel,
            Description = entity.Description
        };

        ViewBag.TransactionId = id;
        return View(await BuildAsync(model));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, LedgerTransactionCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.TransactionId = id;
            return View(await BuildAsync(model));
        }

        var entity = await db.LedgerTransactions.FindAsync(id);
        if (entity is null)
        {
            return NotFound();
        }

        entity.Date = DateTimeHelper.EnsureUtc(model.Date);
        entity.IncomeExpenseCategoryId = model.IncomeExpenseCategoryId;
        entity.Amount = model.Amount;
        entity.PaymentChannel = model.PaymentChannel;
        entity.Description = model.Description;

        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await db.LedgerTransactions.FindAsync(id);
        if (entity is null)
        {
            TempData["ActionError"] = "Kayıt bulunamadı.";
            return RedirectToAction(nameof(Index));
        }

        db.LedgerTransactions.Remove(entity);
        await db.SaveChangesAsync();
        TempData["ActionSuccess"] = "İşlem silindi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportCsv(IFormFile? file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["ImportError"] = "CSV dosyası seciniz.";
            return RedirectToAction(nameof(Index));
        }

        var rows = await CsvImportHelper.ReadRowsAsync(file);
        if (rows.Count < 2)
        {
            TempData["ImportError"] = "CSV baslik ve en az bir veri satırı icermelidir.";
            return RedirectToAction(nameof(Index));
        }

        var headers = BuildHeaders(rows[0]);
        if (!headers.ContainsKey("incomeexpensecategoryid") || !headers.ContainsKey("date") || !headers.ContainsKey("amount"))
        {
            TempData["ImportError"] = "Zorunlu alanlar: IncomeExpenseCategoryId, Date, Amount.";
            return RedirectToAction(nameof(Index));
        }

        var categoryIds = await db.IncomeExpenseCategories.AsNoTracking().Select(x => x.Id).ToListAsync();
        var categorySet = categoryIds.ToHashSet();

        var toAdd = new List<LedgerTransaction>();
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var lineNo = i + 1;

            var categoryIdText = ReadValue(row, headers, "incomeexpensecategoryid");
            if (!int.TryParse(categoryIdText, out var categoryId) || !categorySet.Contains(categoryId))
            {
                TempData["ImportError"] = $"Satir {lineNo}: geçerli IncomeExpenseCategoryId bulunamadı.";
                return RedirectToAction(nameof(Index));
            }

            if (!TryParseDate(ReadValue(row, headers, "date"), out var date))
            {
                TempData["ImportError"] = $"Satir {lineNo}: Date alanı geçersiz.";
                return RedirectToAction(nameof(Index));
            }

            if (!TryParseAmount(ReadValue(row, headers, "amount"), out var amount) || amount <= 0)
            {
                TempData["ImportError"] = $"Satir {lineNo}: Amount alanı geçersiz.";
                return RedirectToAction(nameof(Index));
            }

            if (!TryParsePaymentChannel(ReadValue(row, headers, "paymentchannel"), out var paymentChannel))
            {
                TempData["ImportError"] = $"Satir {lineNo}: PaymentChannel alanı geçersiz.";
                return RedirectToAction(nameof(Index));
            }

            toAdd.Add(new LedgerTransaction
            {
                Date = DateTimeHelper.EnsureUtc(date),
                IncomeExpenseCategoryId = categoryId,
                Amount = amount,
                PaymentChannel = paymentChannel,
                Description = NullIfWhiteSpace(ReadValue(row, headers, "description"))
            });
        }

        db.LedgerTransactions.AddRange(toAdd);
        await db.SaveChangesAsync();
        TempData["ImportSuccess"] = $"{toAdd.Count} gelir-gider kaydı CSV ile eklendi.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<LedgerTransactionCreateViewModel> BuildAsync(LedgerTransactionCreateViewModel model)
    {
        model.CategoryOptions = await db.IncomeExpenseCategories
            .AsNoTracking()
            .Where(x => x.Active)
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .Select(x => new SelectListItem($"{CategoryTypeHelper.Display(x.Type)} - {x.Name}", x.Id.ToString()))
            .ToListAsync();

        return model;
    }

    private static Dictionary<string, int> BuildHeaders(string[] row)
    {
        var map = new Dictionary<string, int>();
        for (var i = 0; i < row.Length; i++)
        {
            var key = NormalizeHeaderKey(row[i]);
            if (!string.IsNullOrWhiteSpace(key))
            {
                map[key] = i;
            }
        }

        return map;
    }

    private static string ReadValue(string[] row, Dictionary<string, int> headers, string key)
    {
        if (!headers.TryGetValue(key, out var idx) || idx >= row.Length)
        {
            return string.Empty;
        }

        return row[idx].Trim();
    }

    private static string NormalizeHeaderKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty);
    }

    private static string? NullIfWhiteSpace(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool TryParseDate(string value, out DateTime date)
    {
        var formats = new[] { "yyyy-MM-dd", "dd.MM.yyyy", "dd/MM/yyyy", "MM/dd/yyyy" };
        return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateTime.TryParse(value, CultureInfo.GetCultureInfo("tr-TR"), DateTimeStyles.None, out date)
            || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryParseAmount(string value, out decimal amount)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("tr-TR"), out amount)
            || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    private static bool TryParsePaymentChannel(string value, out PaymentChannel channel)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            channel = PaymentChannel.Bank;
            return true;
        }

        if (int.TryParse(value, out var asInt) && Enum.IsDefined(typeof(PaymentChannel), asInt))
        {
            channel = (PaymentChannel)asInt;
            return true;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is "cash" or "nakit")
        {
            channel = PaymentChannel.Cash;
            return true;
        }

        if (normalized is "bank" or "banka" or "havale" or "eft")
        {
            channel = PaymentChannel.Bank;
            return true;
        }

        channel = default;
        return false;
    }
}
