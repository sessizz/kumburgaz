using Kumburgaz.Web.Services;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class FlexibleDecimalParserTests
{
    [Theory]
    // Tek nokta + tam 3 basamak: Türkçe binlik ayracı (2.500 TL = iki bin beş yüz).
    [InlineData("2.500", 2500)]
    [InlineData("12.345", 12345)]
    [InlineData("-2.500", -2500)]
    // Çoklu nokta: binlik ayracı.
    [InlineData("1.234.567", 1234567)]
    // Türkçe tam biçim.
    [InlineData("1.234,56", 1234.56)]
    [InlineData("2500,75", 2500.75)]
    // Uygulamanın kendi CSV dışa aktarımı (InvariantCulture) geri okunabilmeli.
    [InlineData("2500.75", 2500.75)]
    [InlineData("1.5", 1.5)]
    [InlineData("12.34", 12.34)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("₺ 1.250,00", 1250)]
    [InlineData("1500", 1500)]
    public void TryParse_reads_turkish_and_invariant_amounts(string input, double expected)
    {
        Assert.True(FlexibleDecimalParser.TryParse(input, out var amount));
        Assert.Equal((decimal)expected, amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    public void TryParse_rejects_empty_or_non_numeric_input(string? input)
    {
        Assert.False(FlexibleDecimalParser.TryParse(input, out _));
    }
}
