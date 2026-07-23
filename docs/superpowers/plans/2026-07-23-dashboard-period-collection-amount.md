# Dashboard Period Collection Amount Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Display the amount collected against the selected dues period inside the dashboard’s “Tahsilat Oranı” card.

**Architecture:** Reuse the existing `collectedInPeriod` calculation that already supplies the collection-rate numerator. Expose it through `DashboardViewModel` and render it as a secondary money line in the existing Razor card, without changing database queries or the collection-rate formula.

**Tech Stack:** ASP.NET Core MVC, C#, Razor, xUnit, Entity Framework Core

## Global Constraints

- The amount is `selected period total accrual - selected period remaining debt`.
- Collection transaction dates do not affect the amount.
- “Tüm dönemler” displays the amount applied across all periods.
- The existing “Bu Ay Gelen Aidatlar” calculation remains unchanged.
- No database field or migration is added.
- The card size and dashboard grid remain unchanged.

---

### Task 1: Wire and render the selected-period collected amount

**Files:**
- Create: `tests/Kumburgaz.Web.Tests/DashboardViewTests.cs`
- Modify: `Models/ViewModels.cs:1028-1040`
- Modify: `Controllers/HomeController.cs:225-235`
- Modify: `Views/Home/Index.cshtml:106-113`

**Interfaces:**
- Consumes: existing local variable `decimal collectedInPeriod` in `HomeController.Index`
- Produces: `DashboardViewModel.CollectedInPeriod : decimal`

- [ ] **Step 1: Write the failing source-contract test**

Create `tests/Kumburgaz.Web.Tests/DashboardViewTests.cs`:

```csharp
using Xunit;

namespace Kumburgaz.Web.Tests;

public class DashboardViewTests
{
    [Fact]
    public void Collection_rate_card_displays_the_selected_period_collected_amount()
    {
        var root = FindProjectRoot();
        var controller = File.ReadAllText(Path.Combine(root, "Controllers", "HomeController.cs"));
        var view = File.ReadAllText(Path.Combine(root, "Views", "Home", "Index.cshtml"));

        Assert.Contains("CollectedInPeriod = collectedInPeriod", controller);
        Assert.Contains("Toplanan:", view);
        Assert.Contains("Money(Model.CollectedInPeriod)", view);
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kumburgaz.Web.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Proje kökü bulunamadı.");
    }
}
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet test tests/Kumburgaz.Web.Tests/Kumburgaz.Web.Tests.csproj --filter "FullyQualifiedName~Collection_rate_card_displays_the_selected_period_collected_amount" --no-restore
```

Expected: FAIL because `HomeController` does not yet assign `CollectedInPeriod` and the card does not contain `Toplanan:`.

- [ ] **Step 3: Add the view-model property**

In `Models/ViewModels.cs`, add the property beside `CollectionRate`:

```csharp
public decimal CollectionRate { get; set; }
public decimal CollectedInPeriod { get; set; }
public decimal TotalGenerated { get; set; }
```

- [ ] **Step 4: Assign the existing calculation to the model**

In the `DashboardViewModel` initializer in `Controllers/HomeController.cs`, add:

```csharp
CollectionRate = collectionRate,
CollectedInPeriod = collectedInPeriod,
TotalGenerated = totalGenerated,
```

- [ ] **Step 5: Render the amount in the card**

In `Views/Home/Index.cshtml`, place this line directly after the percentage:

```cshtml
<div class="text-sm text-gray-600 mt-1">
    Toplanan: <span class="font-bold text-emerald-600 tabular-nums">@Money(Model.CollectedInPeriod)</span>
</div>
```

Keep the existing “Tahsil edilen / toplam tahakkuk” explanation immediately below it.

- [ ] **Step 6: Run the focused test and verify GREEN**

Run:

```powershell
dotnet test tests/Kumburgaz.Web.Tests/Kumburgaz.Web.Tests.csproj --filter "FullyQualifiedName~Collection_rate_card_displays_the_selected_period_collected_amount" --no-restore
```

Expected: PASS, 1 test successful and 0 failed.

- [ ] **Step 7: Run the complete regression suite**

Run:

```powershell
dotnet test tests/Kumburgaz.Web.Tests/Kumburgaz.Web.Tests.csproj --no-restore
```

Expected: all tests pass with 0 failures.

- [ ] **Step 8: Review and commit**

Run:

```powershell
git diff --check
git status --short
git diff -- Models/ViewModels.cs Controllers/HomeController.cs Views/Home/Index.cshtml tests/Kumburgaz.Web.Tests/DashboardViewTests.cs
git add -- Models/ViewModels.cs Controllers/HomeController.cs Views/Home/Index.cshtml tests/Kumburgaz.Web.Tests/DashboardViewTests.cs
git commit -m "feat: show period collection amount on dashboard"
```

Expected: one focused commit containing only the model, controller, Razor card, and regression test.
