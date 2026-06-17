using Kumburgaz.Web.Services;
using System.Globalization;

namespace Kumburgaz.Web.Tests;

public class FlexibleDecimalParserTests
{
    [Theory]
    [InlineData("1234,56", "1234.56")]
    [InlineData("1.234,56", "1234.56")]
    [InlineData("1234.56", "1234.56")]
    [InlineData("1,234.56", "1234.56")]
    [InlineData("1.234.567", "1234567")]
    [InlineData("1.234.567,89", "1234567.89")]
    [InlineData("1,234,567.89", "1234567.89")]
    [InlineData("1 234,56 TL", "1234.56")]
    [InlineData("₺1.234,56", "1234.56")]
    public void TryParse_ShouldAcceptTurkishAndInvariantAmountFormats(string value, string expected)
    {
        var parsed = FlexibleDecimalParser.TryParse(value, out var amount);

        Assert.True(parsed);
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void TryParse_ShouldRejectInvalidAmounts(string value)
    {
        Assert.False(FlexibleDecimalParser.TryParse(value, out _));
    }
}
