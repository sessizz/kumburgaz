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
        Assert.Contains("element.disabled = !isDues;", view);
        Assert.Contains("[data-income-ledger-fields] input, [data-income-ledger-fields] select, [data-income-ledger-fields] textarea", view);
        Assert.Contains("element.disabled = !isLedger;", view);
        Assert.Contains("referenceNo.required = isDues && receiptCheckbox.checked;", view);
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
