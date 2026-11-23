using Kumburgaz.Web.Controllers;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
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

    [Fact]
    public async Task Create_income_creates_positive_ledger_transaction_for_income_category()
    {
        await using var db = CreateDb();
        var bank = new BankAccount
        {
            Name = "Vadeli",
            OpeningBalanceDate = Utc(2026, 1, 1),
            Active = true
        };
        var category = new IncomeExpenseCategory
        {
            Name = "Faiz Geliri",
            Type = CategoryTypeHelper.Gelir,
            Active = true
        };
        db.AddRange(bank, category);
        await db.SaveChangesAsync();

        var controller = CreateController(db, new Dictionary<string, StringValues>
        {
            ["Amount"] = "17015,19"
        });

        await controller.CreateIncome(new CashBankIncomeFormViewModel
        {
            Kind = "bank",
            Id = bank.Id,
            IncomeSource = $"category:{category.Id}",
            Date = Utc(2026, 6, 22),
            Amount = 17_015.19m,
            Description = "FAİZ"
        });

        var transaction = await db.LedgerTransactions.SingleAsync();
        Assert.Equal(category.Id, transaction.IncomeExpenseCategoryId);
        Assert.Equal(17_015.19m, transaction.Amount);
        Assert.Equal(bank.Id, transaction.BankAccountId);
        Assert.Null(transaction.CashBoxId);
    }

    private static async Task<(BankAccount Bank, DuesInstallment Installment)> SeedDuesAsync(ApplicationDbContext db)
    {
        var site = new Site { Name = "Test Site" };
        var block = new Block { Site = site, Name = "C" };
        var unit = new Unit { Block = block, UnitNo = "27", OwnerName = "Malik", Active = true };
        var duesType = new DuesType { Name = "Çift Oda", Amount = 15_000m, Active = true };
        var group = new BillingGroup
        {
            Name = "Çift Oda",
            DuesType = duesType,
            EffectiveStartPeriod = "2026-2027",
            Active = true
        };
        var membership = new BillingGroupUnit
        {
            BillingGroup = group,
            Unit = unit,
            StartPeriod = "2026-2027"
        };
        var installment = new DuesInstallment
        {
            BillingGroup = group,
            Unit = unit,
            Period = "2026-2027",
            AccrualDate = Utc(2026, 7, 12),
            DueDate = Utc(2026, 9, 30),
            Amount = 15_000m,
            RemainingAmount = 15_000m
        };
        var bank = new BankAccount
        {
            Name = "Vadeli",
            OpeningBalanceDate = Utc(2026, 1, 1),
            Active = true
        };
        db.AddRange(site, block, unit, duesType, group, membership, installment, bank);
        await db.SaveChangesAsync();
        return (bank, installment);
    }

    [Fact]
    public async Task Create_income_uses_collection_flow_when_dues_is_selected()
    {
        await using var db = CreateDb();
        var (bank, installment) = await SeedDuesAsync(db);
        var controller = CreateController(db, new Dictionary<string, StringValues>
        {
            ["Amount"] = "3703,77"
        });

        await controller.CreateIncome(new CashBankIncomeFormViewModel
        {
            Kind = "bank",
            Id = bank.Id,
            IncomeSource = "dues",
            BillingGroupId = installment.BillingGroupId,
            DuesInstallmentId = installment.Id,
            Date = Utc(2026, 7, 17),
            Amount = 3_703.77m
        });

        var collection = await db.Collections.SingleAsync();
        var allocation = await db.CollectionAllocations.SingleAsync();
        Assert.Equal(bank.Id, collection.BankAccountId);
        Assert.Equal(installment.Id, allocation.DuesInstallmentId);
        Assert.Equal(3_703.77m, allocation.AppliedAmount);
        Assert.Empty(await db.LedgerTransactions.ToListAsync());
    }

    [Fact]
    public async Task Create_income_rejects_expense_category()
    {
        await using var db = CreateDb();
        var bank = new BankAccount
        {
            Name = "Vadeli",
            OpeningBalanceDate = Utc(2026, 1, 1),
            Active = true
        };
        var category = new IncomeExpenseCategory
        {
            Name = "Banka Masrafı",
            Type = CategoryTypeHelper.Gider,
            Active = true
        };
        db.AddRange(bank, category);
        await db.SaveChangesAsync();

        var controller = CreateController(db, new Dictionary<string, StringValues>
        {
            ["Amount"] = "100"
        });

        await controller.CreateIncome(new CashBankIncomeFormViewModel
        {
            Kind = "bank",
            Id = bank.Id,
            IncomeSource = $"category:{category.Id}",
            Date = Utc(2026, 7, 24),
            Amount = 100m
        });

        Assert.Empty(await db.LedgerTransactions.ToListAsync());
        Assert.Equal("Geçerli bir gelir kategorisi seçin.", controller.TempData["ActionError"]);
    }

    [Theory]
    [InlineData("wallet", true, true, true)]
    [InlineData("bank", true, true, false)]
    [InlineData("bank", false, true, true)]
    [InlineData("bank", true, false, true)]
    [InlineData("cash", false, true, true)]
    [InlineData("cash", true, false, true)]
    public async Task Create_income_rejects_invalid_missing_or_inactive_account(
        string kind,
        bool seedAccount,
        bool active,
        bool usePositiveId)
    {
        await using var db = CreateDb();
        object account = kind == "cash"
            ? new CashBox
            {
                Name = "Ana Kasa",
                OpeningBalanceDate = Utc(2026, 1, 1),
                Active = active
            }
            : new BankAccount
            {
                Name = "Vadeli",
                OpeningBalanceDate = Utc(2026, 1, 1),
                Active = active
            };
        var category = new IncomeExpenseCategory
        {
            Name = "Faiz Geliri",
            Type = CategoryTypeHelper.Gelir,
            Active = true
        };
        db.Add(category);
        if (seedAccount)
        {
            db.Add(account);
        }
        await db.SaveChangesAsync();

        var accountId = account switch
        {
            CashBox cashBox => cashBox.Id,
            BankAccount bankAccount => bankAccount.Id,
            _ => throw new InvalidOperationException()
        };
        var controller = CreateController(db, new Dictionary<string, StringValues>
        {
            ["Amount"] = "100"
        });

        var result = await controller.CreateIncome(new CashBankIncomeFormViewModel
        {
            Kind = kind,
            Id = usePositiveId ? (seedAccount ? accountId : 1234) : 0,
            IncomeSource = $"category:{category.Id}",
            Date = Utc(2026, 7, 24),
            Amount = 100m
        });

        Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
        Assert.Empty(await db.LedgerTransactions.ToListAsync());
        Assert.Equal("Hesap bilgilerini kontrol edin.", controller.TempData["ActionError"]);
    }

    [Fact]
    public async Task Create_income_rejects_inactive_dues_account_without_writes()
    {
        await using var db = CreateDb();
        var (bank, installment) = await SeedDuesAsync(db);
        bank.Active = false;
        await db.SaveChangesAsync();
        var controller = CreateController(db, new Dictionary<string, StringValues>
        {
            ["Amount"] = "3703,77"
        });

        var result = await controller.CreateIncome(new CashBankIncomeFormViewModel
        {
            Kind = "bank",
            Id = bank.Id,
            IncomeSource = "dues",
            BillingGroupId = installment.BillingGroupId,
            DuesInstallmentId = installment.Id,
            Date = Utc(2026, 7, 24),
            Amount = 3_703.77m
        });

        Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
        Assert.Empty(await db.Collections.ToListAsync());
        Assert.Empty(await db.CollectionAllocations.ToListAsync());
        Assert.Equal(15_000m, (await db.DuesInstallments.SingleAsync()).RemainingAmount);
        Assert.Equal("Hesap bilgilerini kontrol edin.", controller.TempData["ActionError"]);
    }

    [Fact]
    public async Task Create_income_rejects_parsed_amount_above_supported_maximum()
    {
        await using var db = CreateDb();
        var bank = new BankAccount
        {
            Name = "Vadeli",
            OpeningBalanceDate = Utc(2026, 1, 1),
            Active = true
        };
        var category = new IncomeExpenseCategory
        {
            Name = "Faiz Geliri",
            Type = CategoryTypeHelper.Gelir,
            Active = true
        };
        db.AddRange(bank, category);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new Dictionary<string, StringValues>
        {
            ["Amount"] = "1000000000"
        });

        var result = await controller.CreateIncome(new CashBankIncomeFormViewModel
        {
            Kind = "bank",
            Id = bank.Id,
            IncomeSource = $"category:{category.Id}",
            Date = Utc(2026, 7, 24),
            Amount = 1_000_000_000m
        });

        Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
        Assert.Empty(await db.LedgerTransactions.ToListAsync());
        Assert.Equal("Gelir bilgilerini kontrol edin.", controller.TempData["ActionError"]);
    }

    [Fact]
    public async Task Create_income_rejects_parsed_amount_below_supported_minimum()
    {
        await using var db = CreateDb();
        var bank = new BankAccount
        {
            Name = "Vadeli",
            OpeningBalanceDate = Utc(2026, 1, 1),
            Active = true
        };
        var category = new IncomeExpenseCategory
        {
            Name = "Faiz Geliri",
            Type = CategoryTypeHelper.Gelir,
            Active = true
        };
        db.AddRange(bank, category);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new Dictionary<string, StringValues>
        {
            ["Amount"] = "0,99"
        });

        var result = await controller.CreateIncome(new CashBankIncomeFormViewModel
        {
            Kind = "bank",
            Id = bank.Id,
            IncomeSource = $"category:{category.Id}",
            Date = Utc(2026, 7, 24),
            Amount = 0.99m
        });

        Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
        Assert.Empty(await db.LedgerTransactions.ToListAsync());
        Assert.Equal("Gelir bilgilerini kontrol edin.", controller.TempData["ActionError"]);
    }

    [Theory]
    [InlineData(true, 10, 20)]
    [InlineData(false, 40, 30)]
    public async Task Import_orders_income_and_incoming_transfer_in_file_order(
        bool transferFirst, int transferTicks, int incomeTicks)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var day = Utc(2026, 11, 23);
        var cash = new CashBox { Name = "Kasa", OpeningBalanceDate = day };
        var bank = new BankAccount { Name = "Banka", OpeningBalanceDate = day };
        var category = new IncomeExpenseCategory { Name = "Faiz", Type = CategoryTypeHelper.Gelir };
        db.AddRange(cash, bank, category, new LedgerTransaction
        {
            CashBox = transferFirst ? cash : null,
            BankAccount = transferFirst ? null : bank,
            PaymentChannel = transferFirst ? PaymentChannel.Cash : PaymentChannel.Bank,
            Date = day.AddTicks(20)
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, new Dictionary<string, StringValues>());

        var transferRow = new CashBankImportRowViewModel
        {
            LineNo = transferFirst ? 2 : 3,
            Type = "transfer",
            Date = "2026-11-23",
            Amount = "100,00",
            ToAccountKey = FinancialAccountHelper.CashKey(cash.Id),
            TransferToCurrentAccount = true,
            Description = "ATM"
        };
        var incomeRow = new CashBankImportRowViewModel
        {
            LineNo = transferFirst ? 3 : 2,
            Type = "income",
            Date = "2026-11-23",
            Amount = "10,00",
            ExpenseCategoryId = category.Id
        };
        var result = await controller.CommitImport(new CashBankImportPreviewViewModel
        {
            Kind = "bank",
            Id = bank.Id,
            Rows = transferFirst ? [transferRow, incomeRow] : [incomeRow, transferRow]
        });

        var redirect = Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
        Assert.Equal(nameof(CashBankController.BankDetail), redirect.ActionName);
        var legs = await db.LedgerTransactions.AsNoTracking().Where(leg => leg.IsTransfer).ToListAsync();
        var outgoing = Assert.Single(legs, leg => !leg.TransferIsIncoming);
        var incoming = Assert.Single(legs, leg => leg.TransferIsIncoming);
        Assert.Equal(cash.Id, outgoing.CashBoxId);
        Assert.Equal(bank.Id, incoming.BankAccountId);
        Assert.NotNull(outgoing.TransferGroupId);
        Assert.Equal(outgoing.TransferGroupId, incoming.TransferGroupId);
        Assert.Equal(day.AddTicks(transferTicks), outgoing.Date);
        Assert.Equal(outgoing.Date, incoming.Date);
        Assert.Equal("Bankaya yatır: ATM", incoming.Description);
        var income = await db.LedgerTransactions.AsNoTracking()
            .SingleAsync(leg => leg.IncomeExpenseCategoryId == category.Id);
        Assert.Equal(day.AddTicks(incomeTicks), income.Date);
    }

    [Fact]
    public async Task Update_transfer_reports_original_same_account_message()
    {
        await using var db = CreateDb();
        var (incoming, outgoing) = await SeedDescriptionOnlyTransferAsync(db);
        var key = FinancialAccountHelper.BankKey(incoming.BankAccountId!.Value);
        var controller = CreateController(db, new Dictionary<string, StringValues> { ["Amount"] = "200,00" });

        var result = await controller.UpdateTransferTransaction(incoming.Id, new CashBankTransferFormViewModel
        {
            Kind = "bank",
            Id = incoming.BankAccountId.Value,
            Date = incoming.Date,
            ToAccountKey = key
        });

        var redirect = Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
        Assert.Equal(nameof(CashBankController.BankDetail), redirect.ActionName);
        Assert.Equal("Kaynak ve karşı hesap aynı olamaz.", controller.TempData["ActionError"]);
        Assert.Equal(100m, incoming.Amount);
        Assert.Equal(100m, outgoing.Amount);
    }

    [Fact]
    public async Task Update_ledger_transaction_reports_missing_account_on_same_day()
    {
        await using var db = CreateDb();
        var category = new IncomeExpenseCategory { Name = "Gider", Type = CategoryTypeHelper.Gider };
        var bank = new BankAccount { Name = "Banka" };
        var tx = new LedgerTransaction
        {
            Date = Utc(2026, 11, 23),
            Amount = 100m,
            BankAccount = bank,
            PaymentChannel = PaymentChannel.Bank,
            IncomeExpenseCategory = category
        };
        db.LedgerTransactions.Add(tx);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new Dictionary<string, StringValues> { ["Amount"] = "200,00" });

        var result = await controller.UpdateLedgerTransaction(tx.Id, new CashBankLedgerFormViewModel
        {
            Kind = "bank",
            Id = bank.Id + 1,
            Date = tx.Date,
            IncomeExpenseCategoryId = category.Id
        });

        var redirect = Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
        Assert.Equal(nameof(CashBankController.BankDetail), redirect.ActionName);
        Assert.Equal("Geçerli bir kasa/banka hesabı seçiniz.", controller.TempData["ActionError"]);
        Assert.Equal(100m, tx.Amount);
        Assert.Equal(bank.Id, tx.BankAccountId);
    }

    [Fact]
    public async Task Update_transfer_preserves_direction_of_category_only_incoming_leg()
    {
        await using var db = CreateDb();
        var cash = new CashBox { Name = "Kasa" };
        var bank = new BankAccount { Name = "Banka" };
        var date = Utc(2026, 7, 24);
        var groupId = Guid.NewGuid();
        var incoming = new LedgerTransaction
        {
            Amount = 100m,
            Date = date,
            BankAccount = bank,
            PaymentChannel = PaymentChannel.Bank,
            IncomeExpenseCategory = new() { Name = "Transfer geliri", Type = CategoryTypeHelper.Gelir },
            TransferGroupId = groupId
        };
        var outgoing = new LedgerTransaction
        {
            Amount = 100m,
            Date = date,
            CashBox = cash,
            PaymentChannel = PaymentChannel.Cash,
            IsTransfer = true,
            TransferGroupId = groupId
        };
        db.AddRange(incoming, outgoing);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new Dictionary<string, StringValues> { ["Amount"] = "200,00" });

        await controller.UpdateTransferTransaction(incoming.Id, new CashBankTransferFormViewModel
        {
            Kind = "bank",
            Id = bank.Id,
            Date = date,
            ToAccountKey = $"cash:{cash.Id}"
        });

        Assert.Equal(200m, incoming.Amount);
        Assert.True(incoming.TransferIsIncoming);
        Assert.Equal(bank.Id, incoming.BankAccountId);
        Assert.Equal(200m, outgoing.Amount);
        Assert.False(outgoing.TransferIsIncoming);
        Assert.Equal(cash.Id, outgoing.CashBoxId);
    }

    [Fact]
    public async Task Update_transfer_edits_both_description_only_legs()
    {
        await using var db = CreateDb();
        var (incoming, outgoing) = await SeedDescriptionOnlyTransferAsync(db);
        var controller = CreateController(db, new Dictionary<string, StringValues> { ["Amount"] = "200,00" });

        await controller.UpdateTransferTransaction(incoming.Id, new CashBankTransferFormViewModel
        {
            Kind = "bank",
            Id = incoming.BankAccountId!.Value,
            Date = incoming.Date,
            ToAccountKey = $"cash:{outgoing.CashBoxId}",
            Description = "Güncellendi"
        });

        db.ChangeTracker.Clear();
        var updated = await db.LedgerTransactions.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, updated.Count);
        Assert.All(updated, leg =>
        {
            Assert.Equal(200m, leg.Amount);
            Assert.Equal("Bankaya yatır: Güncellendi", leg.Description);
            Assert.Null(leg.TransferGroupId);
        });
        Assert.True(updated.Single(x => x.Id == incoming.Id).TransferIsIncoming);
        Assert.False(updated.Single(x => x.Id == outgoing.Id).TransferIsIncoming);
    }

    [Fact]
    public async Task Delete_transaction_removes_both_description_only_transfer_legs()
    {
        await using var db = CreateDb();
        var (_, outgoing) = await SeedDescriptionOnlyTransferAsync(db);
        var controller = CreateController(db, new Dictionary<string, StringValues>());

        await controller.DeleteTransaction("cash", outgoing.CashBoxId!.Value, "ledger", outgoing.Id);

        Assert.Empty(await db.LedgerTransactions.ToListAsync());
    }

    private static async Task<(LedgerTransaction Incoming, LedgerTransaction Outgoing)> SeedDescriptionOnlyTransferAsync(ApplicationDbContext db)
    {
        var date = Utc(2026, 7, 24);
        const string description = "Bankaya yatır: Açıklama";
        var incoming = new LedgerTransaction
        {
            Amount = 100m,
            Date = date,
            Description = description,
            BankAccount = new() { Name = "Banka" },
            PaymentChannel = PaymentChannel.Bank,
            IncomeExpenseCategory = new() { Name = "Diğer gelirler", Type = CategoryTypeHelper.Gelir },
            TransferIsIncoming = true
        };
        var outgoing = new LedgerTransaction
        {
            Amount = 100m,
            Date = date,
            Description = description,
            CashBox = new() { Name = "Kasa" },
            PaymentChannel = PaymentChannel.Cash,
            IncomeExpenseCategory = new() { Name = "Diğer giderler", Type = CategoryTypeHelper.Gider }
        };
        db.AddRange(incoming, outgoing);
        await db.SaveChangesAsync();
        return (incoming, outgoing);
    }

    private static CashBankController CreateController(
        ApplicationDbContext db,
        Dictionary<string, StringValues> formValues)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        httpContext.Request.Form = new FormCollection(formValues);

        var orderService = new TransactionOrderService(db);
        var controller = new CashBankController(
            db,
            new CashBankDetailService(db, new DuesLedgerRowService(db)),
            new CollectionService(db),
            new ImportBatchService(db, new HttpContextAccessor { HttpContext = httpContext }),
            orderService,
            new CashBankTransferService(db, orderService))
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
