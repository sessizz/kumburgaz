using System.Text.Json;
using Kumburgaz.Web.Mcp;
using ModelContextProtocol;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class McpInputTests
{
    [Theory]
    [InlineData("2500", 2500)]
    [InlineData("2500.75", 2500.75)]
    [InlineData("\"2.500,00\"", 2500)]
    [InlineData("\"₺ 1.250,50\"", 1250.5)]
    public void ParseAmount_reads_numbers_and_turkish_text(string json, double expected)
    {
        Assert.Equal((decimal)expected, McpInput.ParseAmount(Json(json)));
    }

    [Theory]
    [InlineData("\"-5\"")]
    [InlineData("\"abc\"")]
    [InlineData("\"\"")]
    [InlineData("\"0\"")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("1e300")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[2500]")]
    [InlineData("{}")]
    public void ParseAmount_rejects_unreadable_or_non_positive_values(string json)
    {
        var ex = Assert.Throws<McpException>(() => McpInput.ParseAmount(Json(json)));
        Assert.Equal("Geçerli bir tutar giriniz.", ex.Message);
    }

    [Fact]
    public void ParseAmount_rejects_missing_value()
    {
        Assert.Throws<McpException>(() => McpInput.ParseAmount(default));
    }

    [Fact]
    public void ParseDate_reads_iso_date_as_utc()
    {
        var date = McpInput.ParseDate("2026-03-05");

        Assert.Equal(new DateTime(2026, 3, 5), date);
        // Npgsql timestamptz sütunlarıyla karşılaştırma ve kayıt UTC ister.
        Assert.Equal(DateTimeKind.Utc, date.Kind);
    }

    [Fact]
    public void ParseDate_defaults_to_today_in_turkey()
    {
        var before = DateTime.UtcNow.AddHours(3).Date;
        var date = McpInput.ParseDate(null);
        var after = DateTime.UtcNow.AddHours(3).Date;

        Assert.InRange(date, before, after);
        Assert.Equal(DateTimeKind.Utc, date.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("05.03.2026")]
    [InlineData("2026-3-5")]
    [InlineData("2026-02-30")]
    [InlineData("2026-03-05T10:00:00")]
    [InlineData("dün")]
    public void ParseDate_rejects_other_formats(string value)
    {
        var ex = Assert.Throws<McpException>(() => McpInput.ParseDate(value));
        Assert.Contains("YYYY-AA-GG", ex.Message);
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData(1, 1)]
    [InlineData(250, 250)]
    [InlineData(500, 500)]
    [InlineData(9999, 500)]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    public void ClampLimit_keeps_limit_between_1_and_500(int? limit, int expected)
    {
        Assert.Equal(expected, McpInput.ClampLimit(limit));
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;
}
