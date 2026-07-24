using ClosedXML.Excel;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class CashBankExcelExportTests
{
    [Fact]
    public async Task Export_includes_all_transactions_not_just_one_page()
    {
        await using var db = CreateDb();
        var bank = new BankAccount
        {
            Name = "Vadeli Hesap",
            OpeningBalance = 1_000m,
            OpeningBalanceDate = Utc(2026, 1, 1),
            Active = true
        };
        var category = new IncomeExpenseCategory
        {
            Name = "Bakım Onarım",
            Type = CategoryTypeHelper.Gider,
            Active = true
        };
        db.AddRange(bank, category);
        await db.SaveChangesAsync();

        // Bir sayfadan (25) fazla işlem: 40 gider hareketi.
        const int txCount = 40;
        for (var i = 1; i <= txCount; i++)
        {
            db.LedgerTransactions.Add(new LedgerTransaction
            {
                Date = Utc(2026, 2, 1).AddDays(i),
                Amount = 100m + i,
                PaymentChannel = PaymentChannel.Bank,
                BankAccountId = bank.Id,
                IncomeExpenseCategoryId = category.Id,
                Description = $"Gider {i}"
            });
        }
        await db.SaveChangesAsync();

        var service = new CashBankDetailService(db, new DuesLedgerRowService(db));
        var vm = await service.BuildAsync("bank", bank.Id, new CashBankDetailQuery { PageSize = int.MaxValue });
        Assert.NotNull(vm);

        // Açılış satırı dahil tüm işlemler gelmeli (40 gider + 1 açılış), sadece 25'lik sayfa değil.
        var exportedRows = vm!.Groups.SelectMany(g => g.Items).ToList();
        Assert.Equal(txCount + 1, exportedRows.Count);

        var bytes = CashBankExcelExportHelper.Build(vm);
        Assert.True(bytes.Length > 1000);

        // Geçerli bir xlsx üretildiğini ve tüm gider satırlarının yer aldığını doğrula.
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(1);
        var giderCells = ws.CellsUsed(c => c.GetString() == "Gider").Count();
        Assert.Equal(txCount, giderCells);

        var titleText = ws.Cell(1, 1).GetString();
        Assert.Contains("Vadeli Hesap", titleText);
    }

    [Fact]
    public async Task Export_respects_type_filter()
    {
        await using var db = CreateDb();
        var bank = new BankAccount
        {
            Name = "Ana Hesap",
            OpeningBalanceDate = Utc(2026, 1, 1),
            Active = true
        };
        var expense = new IncomeExpenseCategory { Name = "Gider", Type = CategoryTypeHelper.Gider, Active = true };
        var income = new IncomeExpenseCategory { Name = "Gelir", Type = CategoryTypeHelper.Gelir, Active = true };
        db.AddRange(bank, expense, income);
        await db.SaveChangesAsync();

        db.LedgerTransactions.Add(new LedgerTransaction
        {
            Date = Utc(2026, 3, 1),
            Amount = 500m,
            PaymentChannel = PaymentChannel.Bank,
            BankAccountId = bank.Id,
            IncomeExpenseCategoryId = expense.Id,
            Description = "Bir gider"
        });
        db.LedgerTransactions.Add(new LedgerTransaction
        {
            Date = Utc(2026, 3, 2),
            Amount = 900m,
            PaymentChannel = PaymentChannel.Bank,
            BankAccountId = bank.Id,
            IncomeExpenseCategoryId = income.Id,
            Description = "Bir gelir"
        });
        await db.SaveChangesAsync();

        var service = new CashBankDetailService(db, new DuesLedgerRowService(db));
        var vm = await service.BuildAsync("bank", bank.Id, new CashBankDetailQuery
        {
            Type = "cikis",
            PageSize = int.MaxValue
        });

        var rows = vm!.Groups.SelectMany(g => g.Items).ToList();
        Assert.Single(rows);
        Assert.Equal(TxKind.Cikis, rows[0].Kind);

        var bytes = CashBankExcelExportHelper.Build(vm);
        Assert.True(bytes.Length > 1000);
    }

    [Fact]
    public async Task Export_with_apostrophe_bounded_account_name_does_not_throw()
    {
        await using var db = CreateDb();
        // Excel çalışma sayfası adı tek tırnakla başlayıp bitemez; SafeSheetName bunu temizlemeli,
        // aksi halde Worksheets.Add hata fırlatır ve dışa aktarma 500 döner.
        var bank = new BankAccount
        {
            Name = "'Vadeli'",
            OpeningBalanceDate = Utc(2026, 1, 1),
            Active = true
        };
        db.Add(bank);
        await db.SaveChangesAsync();

        var service = new CashBankDetailService(db, new DuesLedgerRowService(db));
        var vm = await service.BuildAsync("bank", bank.Id, new CashBankDetailQuery { PageSize = int.MaxValue });

        var exception = Record.Exception(() => { _ = CashBankExcelExportHelper.Build(vm!); });
        Assert.Null(exception);
    }

    [Fact]
    public async Task Export_does_not_list_opening_balance_as_inflow_or_outflow()
    {
        await using var db = CreateDb();
        var bank = new BankAccount
        {
            Name = "Devirli Hesap",
            OpeningBalance = 2_500m,
            OpeningBalanceDate = Utc(2026, 1, 1),
            Active = true
        };
        db.Add(bank);
        await db.SaveChangesAsync();

        var service = new CashBankDetailService(db, new DuesLedgerRowService(db));
        var vm = await service.BuildAsync("bank", bank.Id, new CashBankDetailQuery { PageSize = int.MaxValue });

        var bytes = CashBankExcelExportHelper.Build(vm!);
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(1);

        // Açılış satırının bulunduğu satırda Giriş (6) ve Çıkış (7) boş olmalı, Bakiye (8) = 2.500.
        var openingCell = ws.CellsUsed(c => c.GetString() == "Açılış").FirstOrDefault();
        Assert.NotNull(openingCell);
        var r = openingCell!.Address.RowNumber;
        Assert.True(ws.Cell(r, 6).IsEmpty());
        Assert.True(ws.Cell(r, 7).IsEmpty());
        Assert.Equal(2_500m, ws.Cell(r, 8).GetValue<decimal>());
    }

    [Fact]
    public async Task Export_of_date_range_shows_period_end_balance_not_current_balance()
    {
        await using var db = CreateDb();
        var bank = new BankAccount
        {
            Name = "Ana Hesap",
            OpeningBalanceDate = Utc(2026, 1, 1),
            Active = true
        };
        var income = new IncomeExpenseCategory { Name = "Gelir", Type = CategoryTypeHelper.Gelir, Active = true };
        db.AddRange(bank, income);
        await db.SaveChangesAsync();

        // Şubat'ta 1.000 giriş (aralık içi), Nisan'da 500 giriş (aralık dışı, sonraki).
        db.LedgerTransactions.Add(new LedgerTransaction
        {
            Date = Utc(2026, 2, 10),
            Amount = 1_000m,
            PaymentChannel = PaymentChannel.Bank,
            BankAccountId = bank.Id,
            IncomeExpenseCategoryId = income.Id,
            Description = "Şubat geliri"
        });
        db.LedgerTransactions.Add(new LedgerTransaction
        {
            Date = Utc(2026, 4, 10),
            Amount = 500m,
            PaymentChannel = PaymentChannel.Bank,
            BankAccountId = bank.Id,
            IncomeExpenseCategoryId = income.Id,
            Description = "Nisan geliri"
        });
        await db.SaveChangesAsync();

        var service = new CashBankDetailService(db, new DuesLedgerRowService(db));
        var vm = await service.BuildAsync("bank", bank.Id, new CashBankDetailQuery
        {
            Range = "custom",
            From = new DateOnly(2026, 2, 1),
            To = new DateOnly(2026, 2, 28),
            PageSize = int.MaxValue
        });
        // Hesabın anlık bakiyesi 1.500; ama Şubat aralığının sonu 1.000 olmalı.
        Assert.Equal(1_500m, vm!.Balance);

        var bytes = CashBankExcelExportHelper.Build(vm);
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(1);

        var expectedLabel = "Kapanış Bakiyesi".ToUpperInvariant();
        var labelCell = ws.CellsUsed(c => c.GetString() == expectedLabel).Single();
        var valueCell = ws.Cell(labelCell.Address.RowNumber + 1, labelCell.Address.ColumnNumber);
        Assert.Equal(1_000m, valueCell.GetValue<decimal>());
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static DateTime Utc(int year, int month, int day)
    {
        return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    }
}
