using System.Text.RegularExpressions;
using Xunit;

namespace Kumburgaz.Web.Tests;

// Tailwind/daisyUI derleme zamanında üretilir (wwwroot/css/app.css) ve ikon fontu yalnız kullanılan
// ikonları içeren yerel bir alt kümedir. Görünümde listede olmayan bir ikon adı kullanılırsa
// ekranda ikon yerine düz metin çıkar; bu test o durumu yakalar.
public class StaticStylesTests
{
    private static readonly string Root = FindRepoRoot();

    [Fact]
    public void Every_literal_icon_in_views_is_in_the_icon_font_subset()
    {
        var subset = File.ReadAllLines(Path.Combine(Root, "Styles", "material-symbols-icons.txt"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var span = new Regex("class=\"[^\"]*material-symbols-outlined[^\"]*\"[^>]*>(?<body>[^<]*)<", RegexOptions.Compiled);
        var literal = new Regex(@"^\s*(?<name>[a-z0-9_]+)\s*$", RegexOptions.Compiled);
        // @(kosul ? "ad" : "ad") biçimindeki ikon adları; karşılaştırma değerleri ("created" gibi) sayılmaz.
        var quoted = new Regex("[?:]\\s*\"(?<name>[a-z0-9_]+)\"", RegexOptions.Compiled);

        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in ViewFiles())
        {
            foreach (Match match in span.Matches(File.ReadAllText(file)))
            {
                var body = match.Groups["body"].Value;
                var names = literal.IsMatch(body)
                    ? [literal.Match(body).Groups["name"].Value]
                    : quoted.Matches(body).Select(m => m.Groups["name"].Value).ToArray();
                foreach (var name in names.Where(name => !subset.Contains(name)))
                    missing.Add($"{name} ({Path.GetRelativePath(Root, file)})");
            }
        }

        Assert.True(missing.Count == 0,
            "Styles/material-symbols-icons.txt listesinde olmayan ikonlar (ekleyip tools/update-icon-font.sh çalıştırın): "
            + string.Join(", ", missing));
    }

    [Fact]
    public void Layouts_do_not_load_tailwind_or_icon_font_from_cdn()
    {
        foreach (var layout in new[] { "_Layout.cshtml", "_AuthLayout.cshtml", "_PrintLayout.cshtml" })
        {
            var html = File.ReadAllText(Path.Combine(Root, "Views", "Shared", layout));
            Assert.DoesNotContain("tailwindcss/browser", html);
            Assert.DoesNotContain("cdn.jsdelivr.net/npm/daisyui", html);
            Assert.DoesNotContain("text/tailwindcss", html);
            Assert.DoesNotContain("Material+Symbols", html);
            Assert.Contains("~/css/app.css", html);
            Assert.Contains("~/css/material-symbols.css", html);
        }
        Assert.True(File.Exists(Path.Combine(Root, "wwwroot", "fonts", "material-symbols-outlined.woff2")));
    }

    private static IEnumerable<string> ViewFiles() =>
        new[] { "Views", "Areas" }
            .SelectMany(dir => Directory.EnumerateFiles(Path.Combine(Root, dir), "*.cshtml", SearchOption.AllDirectories));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kumburgaz.Web.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Kumburgaz.Web.csproj bulunamadı.");
    }
}
