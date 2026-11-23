using System.Data.Common;
using System.Globalization;
using Kumburgaz.Web.Controllers;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Kumburgaz.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    [InlineData(true, TimeSpan.TicksPerDay, TimeSpan.TicksPerDay + 10, true)]
    public async Task Import_orders_income_and_incoming_transfer_in_file_order(
        bool transferFirst, long transferTicks, long incomeTicks, bool carryAtMidnight = false)
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
        var seedCashSlot = transferFirst && !carryAtMidnight;
        db.AddRange(cash, bank, category, new LedgerTransaction
        {
            CashBox = seedCashSlot ? cash : null,
            BankAccount = seedCashSlot ? null : bank,
            PaymentChannel = seedCashSlot ? PaymentChannel.Cash : PaymentChannel.Bank,
            Date = day.AddTicks(carryAtMidnight ? TimeSpan.TicksPerDay - 10 : 20)
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
            Date = carryAtMidnight ? "2026-11-24" : "2026-11-23",
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
    public async Task Import_rolls_back_both_transfer_legs_and_batch_rows_when_a_later_batch_row_fails()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var bank = new BankAccount { Name = "Banka" };
        var cash = new CashBox { Name = "Kasa" };
        db.AddRange(bank, cash);
        await db.SaveChangesAsync();
        var controller = CreateController(db, []);
        var counterKey = FinancialAccountHelper.CashKey(cash.Id);

        var result = await controller.CommitImport(new CashBankImportPreviewViewModel
        {
            Kind = "bank", Id = bank.Id,
            Rows =
            [
                new() { LineNo = 2, Date = "2026-11-23", Type = "transfer", Amount = "100", ToAccountKey = counterKey },
                new() { LineNo = 2, Date = "2026-11-23", Type = "transfer", Amount = "200", ToAccountKey = counterKey }
            ]
        });

        Assert.Equal("ImportPreview", Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(result).ViewName);
        Assert.StartsWith("CSV import edilmedi. Import kaydedilemedi:", (string?)controller.TempData["ActionError"]);
        Assert.Empty(await db.LedgerTransactions.AsNoTracking().ToListAsync());
        Assert.Empty(await db.ImportBatchRows.AsNoTracking().ToListAsync());
        var batch = await db.ImportBatches.AsNoTracking().SingleAsync();
        Assert.Equal(ImportBatchStatus.Failed, batch.Status);
        Assert.Null(batch.CommittedAt);
    }

    [Theory]
    [InlineData("2026-11-23", false)]
    [InlineData("2026-11-23T12:00:00+03:00", true)]
    public async Task Import_reallocates_after_reorder_changes_the_existing_slots(string date, bool carryAtMidnight)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var reorderDb = new ApplicationDbContext(options);
        await reorderDb.Database.EnsureCreatedAsync();
        var parsedDay = DateTime.Parse(date, CultureInfo.InvariantCulture).Date;
        var offsetDay = DateTime.SpecifyKind(parsedDay, DateTimeKind.Utc);
        var bank = new BankAccount { Name = "Banka" };
        var cash = new CashBox { Name = "Kasa" };
        var category = new IncomeExpenseCategory { Name = "Faiz", Type = CategoryTypeHelper.Gelir };
        var oldDate = offsetDay.AddTicks(carryAtMidnight ? TimeSpan.TicksPerDay - 10 : 10);
        LedgerTransaction[] existing = Enumerable.Range(0, 3)
            .Select(_ => new LedgerTransaction { BankAccount = bank, Date = oldDate }).ToArray();
        reorderDb.AddRange(existing);
        reorderDb.AddRange(cash, category);
        await reorderDb.SaveChangesAsync();
        reorderDb.ChangeTracker.Clear();
        var bankKey = FinancialAccountHelper.BankKey(bank.Id);
        var order = new TransactionOrderService(reorderDb);
        var reorder = new ReorderBeforeImportBatch(() => order.ReorderDayAsync(bankKey, DateOnly.FromDateTime(offsetDay),
            existing.Reverse().Select(row => new TxRef("ledger", row.Id)).ToArray()));
        var transactions = new ImportTransactionCounter();
        await using var importDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).AddInterceptors(reorder, transactions).Options);
        var controller = CreateController(importDb, []);
        var transferDate = DateTimeHelper.EnsureUtc(parsedDay.AddTicks(40));
        var counterDayChanged = carryAtMidnight
            && DateTimeHelper.EnsureUtc(parsedDay.AddDays(1)).Date != transferDate.Date;
        List<CashBankImportRowViewModel> rows =
        [
            new()
            {
                LineNo = 2, Date = date, Type = "transfer", Amount = "100",
                ToAccountKey = FinancialAccountHelper.CashKey(cash.Id), TransferToCurrentAccount = true
            },
            new() { LineNo = 3, Date = date, Type = "income", Amount = "20", ExpenseCategoryId = category.Id }
        ];
        if (counterDayChanged)
        {
            // Bankanın yeni günü baştan bilinir. İkinci işlem karşı hesabın yeni günü için gereklidir.
            rows.Add(new()
            {
                LineNo = 4, Date = transferDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Type = "income", Amount = "30", ExpenseCategoryId = category.Id
            });
        }

        var result = await controller.CommitImport(new CashBankImportPreviewViewModel
        {
            Kind = "bank", Id = bank.Id, Rows = rows
        });

        Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
        Assert.True(reorder.Ran);
        Assert.Equal(counterDayChanged ? 2 : 1, transactions.Started);
        var persisted = await reorderDb.LedgerTransactions.AsNoTracking().ToListAsync();
        Assert.Equal(offsetDay.AddTicks(30), Assert.Single(persisted, row => row.Id == existing[0].Id).Date);
        var legs = persisted.Where(row => row.IsTransfer).ToArray();
        Assert.Equal(transferDate, Assert.Single(legs, leg => leg.BankAccountId == bank.Id).Date);
        Assert.Equal(transferDate, Assert.Single(legs, leg => leg.CashBoxId == cash.Id).Date);
        Assert.Equal(DateTimeHelper.EnsureUtc(parsedDay.AddTicks(50)),
            Assert.Single(persisted, row => row.IncomeExpenseCategoryId == category.Id && row.Amount == 20m).Date);
        Assert.Equal(rows.Count, await reorderDb.ImportBatchRows.CountAsync());
        Assert.Equal(ImportBatchStatus.Committed, (await reorderDb.ImportBatches.SingleAsync()).Status);
    }

    [Fact]
    public async Task In_flight_collection_append_excludes_reorder_until_its_slot_is_persisted()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"ordering-{Guid.NewGuid():N}", Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared, Pooling = false, DefaultTimeout = 1
        }.ToString();
        await using var keepAlive = new SqliteConnection(connectionString);
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connectionString).Options;
        await using var reorderDb = new ApplicationDbContext(options);
        await reorderDb.Database.EnsureCreatedAsync();
        var day = Utc(2026, 11, 23);
        var (bank, installment) = await SeedDuesAsync(reorderDb);
        var first = new LedgerTransaction { BankAccount = bank, Date = day.AddTicks(10) };
        var second = new LedgerTransaction { BankAccount = bank, Date = day.AddTicks(10) };
        reorderDb.AddRange(first, second);
        await reorderDb.SaveChangesAsync();
        reorderDb.ChangeTracker.Clear();

        var pause = new PauseCollectionTargetQuery();
        await using var appendDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connectionString).AddInterceptors(pause).Options);
        var controller = CreateController(appendDb, new Dictionary<string, StringValues> { ["Amount"] = "100" });
        var append = controller.CreateCollection(new CashBankCollectionFormViewModel
        {
            Kind = "bank", Id = bank.Id, Date = day, BillingGroupId = installment.BillingGroupId,
            DuesInstallmentId = installment.Id
        });

        var order = new TransactionOrderService(reorderDb);
        var key = FinancialAccountHelper.BankKey(bank.Id);
        var date = DateOnly.FromDateTime(day);
        TxRef[] staleRows = [new("ledger", second.Id), new("ledger", first.Id)];
        try
        {
            await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var blocked = await Assert.ThrowsAsync<SqliteException>(() => order.ReorderDayAsync(key, date, staleRows));
            Assert.True(blocked.SqliteErrorCode is 5 or 6, blocked.Message);
        }
        finally
        {
            pause.Resume.TrySetResult();
            await append;
        }

        Assert.Null(controller.TempData["ActionError"]);
        var collection = await reorderDb.Collections.AsNoTracking().SingleAsync();
        Assert.Equal(day.AddTicks(20), collection.Date);
        await Assert.ThrowsAsync<InvalidOperationException>(() => order.ReorderDayAsync(key, date, staleRows));
        await order.ReorderDayAsync(key, date, [.. staleRows, new("collection", collection.Id)]);
        Assert.Equal(day.AddTicks(30), (await reorderDb.Collections.AsNoTracking().SingleAsync()).Date);
    }

    [Theory]
    [InlineData("2026-11-23T12:00:00+03:00")]
    [InlineData("2026-11-23T23:30:00-05:00")]
    public async Task Import_allocates_offset_bearing_dates_from_local_midnight_before_converting_to_utc(string date)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var (bank, installment) = await SeedDuesAsync(db);
        var cash = new CashBox { Name = "Kasa" };
        var category = new IncomeExpenseCategory { Name = "Faiz", Type = CategoryTypeHelper.Gelir };
        db.AddRange(cash, category);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new Dictionary<string, StringValues>());

        var result = await controller.CommitImport(new CashBankImportPreviewViewModel
        {
            Kind = "bank",
            Id = bank.Id,
            Rows =
            [
                new() { LineNo = 2, Date = date, Type = "collection", Amount = "10,00", DuesInstallmentId = installment.Id },
                new() { LineNo = 3, Date = date, Type = "income", Amount = "20,00", ExpenseCategoryId = category.Id },
                new()
                {
                    LineNo = 4, Date = date, Type = "transfer", Amount = "100,00",
                    ToAccountKey = FinancialAccountHelper.CashKey(cash.Id), TransferToCurrentAccount = true
                }
            ]
        });

        var redirect = Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
        Assert.Equal(nameof(CashBankController.BankDetail), redirect.ActionName);
        var utcMidnight = DateTimeOffset.Parse(date, CultureInfo.InvariantCulture)
            .LocalDateTime.Date.ToUniversalTime();
        var collection = await db.Collections.AsNoTracking().SingleAsync();
        Assert.Equal(utcMidnight.AddTicks(10), collection.Date);
        var ledger = await db.LedgerTransactions.AsNoTracking().ToListAsync();
        var income = Assert.Single(ledger, row => row.IncomeExpenseCategoryId == category.Id);
        Assert.Equal(utcMidnight.AddTicks(20), income.Date);
        var transfers = ledger.Where(row => row.IsTransfer).ToList();
        Assert.Equal(2, transfers.Count);
        Assert.All(transfers, row => Assert.Equal(utcMidnight.AddTicks(30), row.Date));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_transfer_reports_original_same_account_message_for_missing_or_inactive_account(bool exists)
    {
        await using var db = CreateDb();
        const int accountId = 42;
        if (exists)
        {
            db.BankAccounts.Add(new BankAccount { Id = accountId, Name = "Banka", Active = false });
            await db.SaveChangesAsync();
        }

        var controller = CreateController(db, new Dictionary<string, StringValues> { ["Amount"] = "200,00" });
        var result = await controller.CreateTransfer(new CashBankTransferFormViewModel
        {
            Kind = "bank",
            Id = accountId,
            Date = Utc(2026, 11, 23),
            ToAccountKey = $"BANK:{accountId}"
        });

        var redirect = Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
        Assert.Equal(nameof(CashBankController.BankDetail), redirect.ActionName);
        Assert.Equal("Kaynak ve hedef hesap aynı olamaz.", controller.TempData["ActionError"]);
        Assert.Empty(await db.LedgerTransactions.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_transfer_reports_original_same_account_message(bool missingAccount)
    {
        await using var db = CreateDb();
        var (incoming, outgoing) = await SeedDescriptionOnlyTransferAsync(db);
        var accountId = incoming.BankAccountId!.Value + (missingAccount ? 1 : 0);
        var controller = CreateController(db, new Dictionary<string, StringValues> { ["Amount"] = "200,00" });

        var result = await controller.UpdateTransferTransaction(incoming.Id, new CashBankTransferFormViewModel
        {
            Kind = "bank",
            Id = accountId,
            Date = incoming.Date,
            ToAccountKey = $"BANK:0{accountId}"
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

    private sealed class ReorderBeforeImportBatch(Func<Task> reorder) : DbCommandInterceptor
    {
        public bool Ran { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            // İlk sıra okuması tamamlandı. CSV satırları yazılmadan önce kullanıcı sıralamayı kaydeder.
            if (!Ran && command.CommandText.Contains("SELECT COUNT(*)", StringComparison.Ordinal)
                && command.CommandText.Contains("FROM \"ImportBatches\"", StringComparison.Ordinal))
            {
                Ran = true;
                await reorder();
            }

            return result;
        }
    }

    private sealed class ImportTransactionCounter : DbTransactionInterceptor
    {
        public int Started { get; private set; }

        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            Started++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class PauseCollectionTargetQuery : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            // CollectionService hedefi sıra okunduktan sonra, tahsilat yazılmadan önce çözer.
            if (command.CommandText.Contains("FROM \"DuesInstallments\"", StringComparison.Ordinal) && Entered.TrySetResult())
            {
                await Resume.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
