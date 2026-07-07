using Kumburgaz.Web.Controllers;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class CashBankControllerTests
{
    [Fact]
    public async Task Update_ledger_transaction_accepts_income_category_for_income_row()
    {
        await using var db = CreateDb();
        var bank = new BankAccount
        {
            Name = "Vadeli",
            OpeningBalanceDate = Utc(2026, 1, 1),
            Active = true
        };
        var oldIncomeCategory = new IncomeExpenseCategory
        {
            Name = "Faiz Geliri",
            Type = CategoryTypeHelper.Gelir,
            Active = true
        };
        var newIncomeCategory = new IncomeExpenseCategory
        {
            Name = "Vadeli Faiz Geliri",
            Type = CategoryTypeHelper.Gelir,
            Active = true
        };
        var expenseCategory = new IncomeExpenseCategory
        {
            Name = "Banka Masrafi",
            Type = CategoryTypeHelper.Gider,
            Active = true
        };
        var tx = new LedgerTransaction
        {
            Date = Utc(2026, 6, 22),
            Amount = 20_624.47m,
            PaymentChannel = PaymentChannel.Bank,
            BankAccount = bank,
            IncomeExpenseCategory = oldIncomeCategory,
            Description = "FAIZ"
        };

        db.AddRange(bank, oldIncomeCategory, newIncomeCategory, expenseCategory, tx);
        await db.SaveChangesAsync();

        var controller = CreateController(db, new Dictionary<string, StringValues>
        {
            ["Amount"] = "20624,47"
        });

        await controller.UpdateLedgerTransaction(tx.Id, new CashBankLedgerFormViewModel
        {
            Kind = "bank",
            Id = bank.Id,
            Date = Utc(2026, 6, 22),
            Amount = 20_624.47m,
            IncomeExpenseCategoryId = newIncomeCategory.Id,
            Description = "FAIZ"
        });

        var updated = await db.LedgerTransactions
            .Include(x => x.IncomeExpenseCategory)
            .SingleAsync(x => x.Id == tx.Id);
        Assert.Equal(newIncomeCategory.Id, updated.IncomeExpenseCategoryId);
        Assert.Equal(CategoryTypeHelper.Gelir, updated.IncomeExpenseCategory!.Type);
    }

    private static CashBankController CreateController(
        ApplicationDbContext db,
        Dictionary<string, StringValues> formValues)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        httpContext.Request.Form = new FormCollection(formValues);

        var controller = new CashBankController(
            db,
            new CashBankDetailService(db),
            new CollectionService(db),
            new ImportBatchService(db, new HttpContextAccessor { HttpContext = httpContext }))
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = httpContext
            },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };

        return controller;
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

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
