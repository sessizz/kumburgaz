using Xunit;

namespace Kumburgaz.Web.Tests;

public class DocumentViewTests
{
    [Fact]
    public void Document_form_uses_multipart_multiple_upload_and_has_no_url_input()
    {
        var markup = File.ReadAllText(Path.Combine(
            FindProjectRoot(),
            "Views",
            "Documents",
            "_DocumentForm.cshtml"));

        Assert.Contains("enctype=\"multipart/form-data\"", markup);
        Assert.Contains("name=\"files\"", markup);
        Assert.Contains("multiple", markup);
        Assert.DoesNotContain("asp-for=\"Url\"", markup);
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
