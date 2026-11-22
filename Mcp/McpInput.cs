using System.Globalization;
using System.Text.Json;
using Kumburgaz.Web.Services;
using ModelContextProtocol;

namespace Kumburgaz.Web.Mcp;

// Araç girdilerinin ortak okunması; hatalar Claude'a Türkçe mesajla döner.
public static class McpInput
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    // Türkiye 2016'dan beri sabit UTC+3 (yaz saati yok). Konteyner UTC çalışır;
    // gece 00:00-03:00 arasında sunucunun "bugün"ü bir gün geride kalır.
    private static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);

    // Sayı ya da Türkçe metin ("2.500,00"); sıfırdan büyük olmalı.
    public static decimal ParseAmount(JsonElement amount)
    {
        decimal value = 0m;
        var readable = amount.ValueKind switch
        {
            JsonValueKind.Number => amount.TryGetDecimal(out value),
            JsonValueKind.String => FlexibleDecimalParser.TryParse(amount.GetString(), out value),
            _ => false
        };

        return readable && value > 0m
            ? value
            : throw new McpException("Geçerli bir tutar giriniz.");
    }

    // YYYY-AA-GG; verilmezse Türkiye'de bugün. Sonuç, uygulamadaki tarihler gibi UTC gece yarısıdır.
    public static DateTime ParseDate(string? value)
    {
        if (value is null)
        {
            return DateTimeHelper.EnsureUtc(DateTime.UtcNow.Add(TurkeyOffset).Date);
        }

        return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? DateTimeHelper.EnsureUtc(date)
            : throw new McpException("Tarihi YYYY-AA-GG biçiminde giriniz.");
    }

    public static int ClampLimit(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
}
