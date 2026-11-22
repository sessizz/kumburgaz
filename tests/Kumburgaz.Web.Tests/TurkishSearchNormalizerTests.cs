using Kumburgaz.Web.Services;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class TurkishSearchNormalizerTests
{
    [Theory]
    [InlineData("Şahİn")]
    [InlineData("SAHIN")]
    [InlineData("şahin")]
    [InlineData("Şahın")]
    public void Turkish_letters_fold_to_ascii(string value)
    {
        Assert.Equal("sahin", TurkishSearchNormalizer.Normalize(value));
    }

    [Theory]
    [InlineData("A-5")]
    [InlineData("a blok 5")]
    [InlineData("A5")]
    [InlineData("A Blok-5")]
    public void Block_and_unit_number_variants_share_one_key(string value)
    {
        Assert.Equal("a5", TurkishSearchNormalizer.Normalize(value));
    }

    [Fact]
    public void Phone_formats_share_one_key()
    {
        Assert.Equal(
            TurkishSearchNormalizer.Normalize("05321112233"),
            TurkishSearchNormalizer.Normalize("0532 111 22 33"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  - ")]
    [InlineData("blok")]
    public void Empty_input_gives_empty_key(string? value)
    {
        Assert.Equal(string.Empty, TurkishSearchNormalizer.Normalize(value));
    }
}
