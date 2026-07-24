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
