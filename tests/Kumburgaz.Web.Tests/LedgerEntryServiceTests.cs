using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class LedgerEntryServiceTests
{
    [Fact]
    public async Task CreateAsync_writes_expense_with_resolved_bank_account()
    {
        await using var db = CreateDb();
        var (expenseId, _, bankId) = Seed(db);
        var service = new LedgerEntryService(db);

        var id = await service.CreateAsync(
            new LedgerEntryRequest(new DateTime(2026, 3, 5), expenseId, 150m, $"bank:{bankId}", "Test"),
            LedgerEntryKind.Expense);

        var saved = await db.LedgerTransactions.SingleAsync(x => x.Id == id);
        Assert.Equal(PaymentChannel.Bank, saved.PaymentChannel);
        Assert.Equal(bankId, saved.BankAccountId);
        Assert.Null(saved.CashBoxId);
        Assert.Equal(DateTimeKind.Utc, saved.Date.Kind);
        Assert.Equal(150m, saved.Amount);
        Assert.Equal(expenseId, saved.IncomeExpenseCategoryId);
    }

    [Fact]
    public async Task PreviewAsync_does_not_write()
    {
        await using var db = CreateDb();
        var (expenseId, _, bankId) = Seed(db);
        var service = new LedgerEntryService(db);

        var preview = await service.PreviewAsync(
            new LedgerEntryRequest(new DateTime(2026, 3, 5), expenseId, 150m, $"bank:{bankId}", null));

        Assert.Equal(0, await db.LedgerTransactions.CountAsync());
        Assert.Equal(LedgerEntryKind.Expense, preview.Kind);
        Assert.False(string.IsNullOrWhiteSpace(preview.AccountName));
        Assert.False(string.IsNullOrWhiteSpace(preview.CategoryName));
    }

    [Fact]
    public async Task CreateAsync_rejects_income_category_when_expense_expected()
    {
        await using var db = CreateDb();
        var (_, incomeId, bankId) = Seed(db);
        var service = new LedgerEntryService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            new LedgerEntryRequest(new DateTime(2026, 3, 5), incomeId, 150m, $"bank:{bankId}", null),
            LedgerEntryKind.Expense));
        Assert.Equal(0, await db.LedgerTransactions.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_rejects_unknown_account_key()
    {
        await using var db = CreateDb();
        var (expenseId, _, _) = Seed(db);
        var service = new LedgerEntryService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            new LedgerEntryRequest(new DateTime(2026, 3, 5), expenseId, 150m, "bank:999", null)));
    }

    private static (int ExpenseId, int IncomeId, int BankId) Seed(ApplicationDbContext db)
    {
        var expense = new IncomeExpenseCategory { Name = "Bakım", Type = "Expense" };
        var income = new IncomeExpenseCategory { Name = "Kira", Type = "Income" };
        var bank = new BankAccount { Name = "İş Bankası" };
        db.IncomeExpenseCategories.AddRange(expense, income);
        db.BankAccounts.Add(bank);
        db.SaveChanges();
        return (expense.Id, income.Id, bank.Id);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
