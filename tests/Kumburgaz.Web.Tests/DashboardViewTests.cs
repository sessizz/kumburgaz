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
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Kumburgaz.Web.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Proje kökü bulunamadı.");
    }
}
