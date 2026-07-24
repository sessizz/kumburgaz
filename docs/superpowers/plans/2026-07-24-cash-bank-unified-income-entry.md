# Kasa/Banka Birleşik Gelir Girişi Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Kasa/Banka hesap detayındaki “Tahsilat” girişini, aidat tahsilatı ile diğer gelir kategorilerini tek modalda yöneten “Gelir” akışına dönüştürmek.

**Architecture:** Yeni `CashBankIncomeFormViewModel`, `dues` veya `category:<id>` kaynak belirtecini ortak tarih/tutar alanlarıyla taşır. `CashBankController.CreateIncome` aidat seçiminde mevcut `CollectionService` akışını, normal gelir seçiminde pozitif `LedgerTransaction` akışını kullanır; Razor modalı kategori seçimine göre aidat ve normal gelir alanlarını açıp kapatır.

**Tech Stack:** ASP.NET Core MVC, Razor, Entity Framework Core, xUnit, vanilla JavaScript, Tailwind/DaisyUI.

## Global Constraints

- “Aidat” davranışı kategori adına bağlı olmayacak; özel `dues` değeri kullanılacak.
- Normal gelir kaydı yalnızca aktif ve `Gelir` tipindeki kategoriyle oluşturulacak.
- Aidat kaydı mevcut `CollectionService` üzerinden oluşturulacak; tahsisat ve makbuz davranışları korunacak.
- Mevcut `CreateCollection` ve kayıt düzenleme action’ları geriye uyumluluk için korunacak.
- CSV import akışı ve kategori şeması değiştirilmeyecek.

---

### Task 1: Birleşik gelir formu ve controller yönlendirmesi

**Files:**
- Modify: `Models/ViewModels.cs:746-837`
- Modify: `Controllers/CashBankController.cs:613-767`
- Modify: `tests/Kumburgaz.Web.Tests/CashBankControllerTests.cs`

**Interfaces:**
- Consumes: `ICollectionService.CreateAsync(CollectionCreateViewModel)`, `CategoryTypeHelper.Gelir`, `BuildNextTransactionDateAsync(string, int, DateTime)`.
- Produces: `CashBankIncomeFormViewModel` ve `CashBankController.CreateIncome(CashBankIncomeFormViewModel)`.

- [ ] **Step 1: Normal gelir oluşturma testini yaz**

`CashBankControllerTests` içine aşağıdaki testi ekle:

```csharp
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
```

- [ ] **Step 2: Testi RED olarak doğrula**

Run:

```powershell
dotnet test tests\Kumburgaz.Web.Tests\Kumburgaz.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~Create_income_creates_positive_ledger_transaction_for_income_category"
```

Expected: Derleme `CashBankIncomeFormViewModel` veya `CreateIncome` bulunamadığı için başarısız olur.

- [ ] **Step 3: Aidat seçiminin Collection oluşturduğunu test et**

Test sınıfına aidat ilişkilerini oluşturan yardımcıyı ve testi ekle:

```csharp
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
```

- [ ] **Step 4: Gider kategorisinin reddedildiğini test et**

```csharp
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
```

- [ ] **Step 5: Birleşik form modelini ekle**

`Models/ViewModels.cs` içine ekle:

```csharp
public class CashBankIncomeFormViewModel
{
    [Required]
    public string Kind { get; set; } = "bank";

    [Required]
    public int Id { get; set; }

    [Required]
    public string IncomeSource { get; set; } = string.Empty;

    public int? DuesInstallmentId { get; set; }
    public int BillingGroupId { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateTime Date { get; set; } = DateTime.Today;

    [Range(1, 999999999)]
    public decimal Amount { get; set; }

    [MaxLength(80)]
    public string? ReferenceNo { get; set; }

    public bool IsReceipt { get; set; }

    [MaxLength(250)]
    public string? Note { get; set; }

    [MaxLength(250)]
    public string? Description { get; set; }
}
```

- [ ] **Step 6: `CreateIncome` action’ını uygula**

`CashBankController` içinde `CreateCollection` öncesine ekle:

```csharp
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> CreateIncome(CashBankIncomeFormViewModel model)
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
        TempData["ActionError"] = "Gelir bilgilerini kontrol edin.";
        return RedirectToDetail(model.Kind, model.Id);
    }

    var orderedDate = await BuildNextTransactionDateAsync(model.Kind, model.Id, model.Date);
    if (model.IncomeSource == "dues")
    {
        if (!model.DuesInstallmentId.HasValue || model.BillingGroupId <= 0)
        {
            TempData["ActionError"] = "Dönem ve aidat seçin.";
            return RedirectToDetail(model.Kind, model.Id);
        }

        try
        {
            var collectionId = await collectionService.CreateAsync(new CollectionCreateViewModel
            {
                BillingGroupId = model.BillingGroupId,
                DuesInstallmentId = model.DuesInstallmentId,
                Date = orderedDate,
                Amount = model.Amount,
                PaymentChannel = model.Kind == "bank" ? PaymentChannel.Bank : PaymentChannel.Cash,
                AccountKey = BuildAccountKey(model.Kind, model.Id),
                ReferenceNo = model.ReferenceNo,
                IsReceipt = model.IsReceipt,
                Note = model.Note
            });
            TempData["ActionSuccess"] = "Aidat tahsilatı kaydedildi.";

            if (model.IsReceipt)
            {
                var returnUrl = Url.Action(
                    model.Kind == "bank" ? nameof(BankDetail) : nameof(CashBoxDetail),
                    new { id = model.Id });
                return RedirectToAction(
                    "PrintReceipt",
                    "Collections",
                    new { id = collectionId, returnUrl });
            }
        }
        catch (Exception ex)
        {
            TempData["ActionError"] = ex.Message;
        }

        return RedirectToDetail(model.Kind, model.Id);
    }

    const string categoryPrefix = "category:";
    if (!model.IncomeSource.StartsWith(categoryPrefix, StringComparison.Ordinal) ||
        !int.TryParse(model.IncomeSource[categoryPrefix.Length..], out var categoryId))
    {
        TempData["ActionError"] = "Geçerli bir gelir kategorisi seçin.";
        return RedirectToDetail(model.Kind, model.Id);
    }

    var category = await db.IncomeExpenseCategories
        .AsNoTracking()
        .FirstOrDefaultAsync(x =>
            x.Id == categoryId &&
            x.Active &&
            x.Type == CategoryTypeHelper.Gelir);
    if (category is null)
    {
        TempData["ActionError"] = "Geçerli bir gelir kategorisi seçin.";
        return RedirectToDetail(model.Kind, model.Id);
    }

    db.LedgerTransactions.Add(new LedgerTransaction
    {
        Date = DateTimeHelper.EnsureUtc(orderedDate),
        IncomeExpenseCategoryId = category.Id,
        Amount = model.Amount,
        PaymentChannel = model.Kind == "bank" ? PaymentChannel.Bank : PaymentChannel.Cash,
        CashBoxId = model.Kind == "cash" ? model.Id : null,
        BankAccountId = model.Kind == "bank" ? model.Id : null,
        Description = string.IsNullOrWhiteSpace(model.Description)
            ? category.Name
            : model.Description
    });
    await db.SaveChangesAsync();
    TempData["ActionSuccess"] = "Gelir kaydedildi.";
    return RedirectToDetail(model.Kind, model.Id);
}
```

- [ ] **Step 7: Controller testlerini GREEN olarak doğrula**

Run:

```powershell
dotnet test tests\Kumburgaz.Web.Tests\Kumburgaz.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~CashBankControllerTests"
```

Expected: `CashBankControllerTests` içindeki tüm testler PASS.

- [ ] **Step 8: Controller ve model değişikliklerini commit et**

```powershell
git add Models/ViewModels.cs Controllers/CashBankController.cs tests/Kumburgaz.Web.Tests/CashBankControllerTests.cs
git commit -m "feat: add unified cash bank income endpoint"
```

### Task 2: Gelir butonu ve koşullu birleşik modal

**Files:**
- Modify: `Views/CashBank/_DetailParts/_SidePanel.cshtml:34-82`
- Modify: `Views/CashBank/_DetailParts/_SidePanel.cshtml:179-245`
- Modify: `Views/CashBank/_DetailParts/_SidePanel.cshtml:407-470`
- Create: `tests/Kumburgaz.Web.Tests/CashBankViewTests.cs`

**Interfaces:**
- Consumes: `CashBankDetailViewModel.IncomeCategoryOptions`, `CashBankDetailViewModel.PeriodOptions`, `CashBankDetailViewModel.DuesInstallmentOptions`, `CashBankController.CreateIncome`.
- Produces: `incomeModal`, `IncomeSource` değerleri `dues` ve `category:<id>`, `syncIncomeFields()`.

- [ ] **Step 1: Görünüm sözleşme testini yaz**

`tests/Kumburgaz.Web.Tests/CashBankViewTests.cs`:

```csharp
using Xunit;

namespace Kumburgaz.Web.Tests;

public class CashBankViewTests
{
    [Fact]
    public void Side_panel_exposes_unified_income_entry()
    {
        var root = FindProjectRoot();
        var view = File.ReadAllText(Path.Combine(
            root,
            "Views",
            "CashBank",
            "_DetailParts",
            "_SidePanel.cshtml"));

        Assert.Contains("asp-action=\"CreateIncome\"", view);
        Assert.Contains("incomeModal.showModal()", view);
        Assert.Contains(">Gelir<", view);
        Assert.Contains("value=\"dues\">Aidat", view);
        Assert.Contains("Model.IncomeCategoryOptions", view);
        Assert.Contains("value=\"category:@option.Value\"", view);
        Assert.Contains("data-income-dues-fields", view);
        Assert.Contains("data-income-ledger-fields", view);
        Assert.Contains("syncIncomeFields", view);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Kumburgaz.Web.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Proje kökü bulunamadı.");
    }
}
```

- [ ] **Step 2: Görünüm testini RED olarak doğrula**

Run:

```powershell
dotnet test tests\Kumburgaz.Web.Tests\Kumburgaz.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~Side_panel_exposes_unified_income_entry"
```

Expected: `CreateIncome` veya `incomeModal` metni bulunamadığı için FAIL.

- [ ] **Step 3: Buton ve menü metinlerini değiştir**

`_SidePanel.cshtml` içinde:

```cshtml
<li><button type="button" onclick="incomeModal.showModal()">Gelir</button></li>
```

Kısayol dizilerini:

```cshtml
var shortcuts = Model.Kind == "bank"
    ? new[] { ("south_west", "Gelir"), ("north_east", "Ödeme"), ("swap_horiz", "Transfer") }
    : new[] { ("south_west", "Gelir"), ("north_east", "Ödeme"), ("upload", "Bankaya Yatır") };
```

Kısayol click eşlemesini:

```cshtml
var click = label == "Gelir"
    ? "incomeModal.showModal()"
    : label == "Ödeme"
        ? "openLedgerModal(false)"
        : "transferModal.showModal()";
```

- [ ] **Step 4: Tahsilat modalını birleşik gelir modalına dönüştür**

Modal formunu `id="incomeModal"` ve `asp-action="CreateIncome"` kullanacak şekilde değiştir. Başlıktan sonra kategori alanını ekle:

```cshtml
<fieldset class="fieldset">
    <legend class="fieldset-legend">Gelir kategorisi</legend>
    <select name="IncomeSource"
            class="select select-bordered w-full"
            required
            data-income-source
            onchange="syncIncomeFields()">
        <option value="">Seçiniz</option>
        <option value="dues">Aidat</option>
        @foreach (var option in Model.IncomeCategoryOptions)
        {
            <option value="category:@option.Value">@option.Text</option>
        }
    </select>
</fieldset>
```

Mevcut `BillingGroupId`, dönem ve aidat seçicisini şu kapsayıcıya al:

```cshtml
<div class="hidden space-y-4" data-income-dues-fields>
    <input type="hidden" name="BillingGroupId" data-dues-billing-group />
    <fieldset class="fieldset">
        <legend class="fieldset-legend">Dönem</legend>
        <select class="select select-bordered w-full" data-dues-period>
            @foreach (var option in Model.PeriodOptions)
            {
                <option value="@option.Value" selected="@option.Selected">@option.Text</option>
            }
        </select>
    </fieldset>
    <fieldset class="fieldset">
        <legend class="fieldset-legend">Daire / Malik</legend>
        <label class="input input-bordered flex items-center gap-2 mb-2">
            <span class="material-symbols-outlined ms-sm text-gray-400">search</span>
            <input type="search"
                   data-dues-search
                   placeholder="Daire no, blok veya isim ara"
                   class="grow" />
        </label>
        <select name="DuesInstallmentId"
                class="select select-bordered w-full"
                data-dues-select>
            <option value="">Daire veya malik seçin</option>
            @foreach (var option in Model.DuesInstallmentOptions)
            {
                <option value="@option.Id"
                        data-billing-group-id="@option.BillingGroupId"
                        data-amount="@option.RemainingAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)"
                        data-period="@option.Period"
                        data-search="@option.SearchText">@option.Text</option>
            }
        </select>
    </fieldset>
</div>
```

Ortak tarih/tutar alanlarından sonra normal gelir açıklamasını ekle:

```cshtml
<fieldset class="fieldset hidden" data-income-ledger-fields>
    <legend class="fieldset-legend">Açıklama</legend>
    <textarea name="Description"
              class="textarea textarea-bordered w-full"
              rows="2"
              placeholder="Gelir açıklaması"></textarea>
</fieldset>
```

Makbuz ve referans/not alanlarını şu kapsayıcıya al:

```cshtml
<div class="hidden space-y-4" data-income-dues-fields>
    <fieldset class="fieldset">
        <label class="label cursor-pointer gap-2 px-0 w-fit">
            <input type="checkbox"
                   id="createCollectionIsReceipt"
                   name="IsReceipt"
                   value="true"
                   class="toggle toggle-sm toggle-primary" />
            <span class="label-text">Makbuz</span>
        </label>
    </fieldset>
    <fieldset class="fieldset">
        <legend id="createCollectionReferenceNoLegend" class="fieldset-legend">Referans / Not</legend>
        <input name="ReferenceNo"
               id="createCollectionReferenceNo"
               class="input input-bordered w-full mb-2"
               placeholder="Referans no" />
        <textarea name="Note"
                  class="textarea textarea-bordered w-full"
                  rows="2"
                  placeholder="Not"></textarea>
    </fieldset>
</div>
```

Modal kapatma düğmelerini `incomeModal.close()` kullanacak şekilde değiştir.

- [ ] **Step 5: Koşullu alan JavaScript’ini ekle**

Mevcut `openLedgerModal` fonksiyonundan önce:

```javascript
function syncIncomeFields() {
    var modal = document.getElementById('incomeModal');
    if (!modal) return;

    var source = modal.querySelector('[data-income-source]');
    var isDues = source && source.value === 'dues';
    var isLedger = source && source.value.startsWith('category:');

    modal.querySelectorAll('[data-income-dues-fields]').forEach(function(element) {
        element.classList.toggle('hidden', !isDues);
    });
    modal.querySelectorAll('[data-income-ledger-fields]').forEach(function(element) {
        element.classList.toggle('hidden', !isLedger);
    });

    var duesSelect = modal.querySelector('[data-dues-select]');
    if (duesSelect) duesSelect.required = isDues;
}
```

Script sonunda ilk durumu kur:

```javascript
syncIncomeFields();
```

- [ ] **Step 6: Görünüm testini GREEN olarak doğrula**

Run:

```powershell
dotnet test tests\Kumburgaz.Web.Tests\Kumburgaz.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~Side_panel_exposes_unified_income_entry"
```

Expected: PASS.

- [ ] **Step 7: Görünüm değişikliklerini commit et**

```powershell
git add Views/CashBank/_DetailParts/_SidePanel.cshtml tests/Kumburgaz.Web.Tests/CashBankViewTests.cs
git commit -m "feat: replace collection shortcut with income entry"
```

### Task 3: Tam doğrulama

**Files:**
- Verify: `Controllers/CashBankController.cs`
- Verify: `Models/ViewModels.cs`
- Verify: `Views/CashBank/_DetailParts/_SidePanel.cshtml`
- Verify: `tests/Kumburgaz.Web.Tests/CashBankControllerTests.cs`
- Verify: `tests/Kumburgaz.Web.Tests/CashBankViewTests.cs`

**Interfaces:**
- Consumes: Task 1 ve Task 2 çıktıları.
- Produces: Derlenen, testleri geçen birleşik gelir girişi.

- [ ] **Step 1: Tam test paketini çalıştır**

```powershell
dotnet test tests\Kumburgaz.Web.Tests\Kumburgaz.Web.Tests.csproj --no-restore
```

Expected: Bütün testler PASS; başarısız test sayısı `0`.

- [ ] **Step 2: Diff ve çalışma ağacını denetle**

```powershell
git diff --check
git status --short --branch
git log -5 --oneline --decorate
```

Expected: Whitespace hatası yok; yalnızca planlanan dosyalar değişmiş veya planlanan commit’ler oluşmuş.

- [ ] **Step 3: Kalan doğrulama değişiklikleri varsa commit et**

Yalnızca Task 1–2 kapsamındaki doğrulama düzeltmeleri varsa:

```powershell
git add Controllers/CashBankController.cs Models/ViewModels.cs Views/CashBank/_DetailParts/_SidePanel.cshtml tests/Kumburgaz.Web.Tests/CashBankControllerTests.cs tests/Kumburgaz.Web.Tests/CashBankViewTests.cs
git commit -m "test: verify unified cash bank income entry"
```

Değişiklik yoksa yeni commit oluşturma.
