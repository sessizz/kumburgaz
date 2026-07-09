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

    [Fact]
    public void Document_detail_loads_jszip_before_docx_preview()
    {
        var markup = File.ReadAllText(Path.Combine(
            FindProjectRoot(),
            "Views",
            "Documents",
            "Details.cshtml"));

        var jsZipIndex = markup.IndexOf("~/lib/jszip/jszip.min.js", StringComparison.Ordinal);
        var docxPreviewIndex = markup.IndexOf("~/lib/docx-preview/docx-preview.min.js", StringComparison.Ordinal);

        Assert.True(jsZipIndex >= 0, "Belge detayinda JSZip yuklenmelidir.");
        Assert.True(docxPreviewIndex > jsZipIndex, "JSZip, docx-preview dosyasindan once yuklenmelidir.");
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
