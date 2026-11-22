using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Kumburgaz.Web.Services;
using ModelContextProtocol;

namespace Kumburgaz.Web.Mcp;

// Araç girdilerinin ortak okunması; hatalar Claude'a Türkçe mesajla döner.
public static class McpInput
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    // Formlardaki [Range] üst sınırı.
    private const decimal MaxAmount = 999_999_999m;

    // Türkiye 2016'dan beri sabit UTC+3 (yaz saati yok). Konteyner UTC çalışır;
    // gece 00:00-03:00 arasında sunucunun "bugün"ü bir gün geride kalır.
    private static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);

    // Sayı ya da Türkçe metin ("2.500,00"); sıfırdan büyük, en fazla 2 ondalık (tutar sütunları (18,2)).
    public static decimal ParseAmount(JsonElement amount)
    {
        decimal value = 0m;
        var readable = amount.ValueKind switch
        {
            JsonValueKind.Number => amount.TryGetDecimal(out value),
            JsonValueKind.String => FlexibleDecimalParser.TryParse(amount.GetString(), out value),
            _ => false
        };

        if (!readable || value is <= 0m or > MaxAmount)
        {
            throw new McpException("Geçerli bir tutar giriniz.");
        }

        // Ölçek hep 2: 2500 ile 2500,00 aynı girdi sayılsın (bkz. Fingerprint).
        var rounded = decimal.Round(value, 2) + 0.00m;
        return rounded == value
            ? rounded
            : throw new McpException("Tutar en fazla 2 ondalık basamak içerebilir.");
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

    // Kırpılmış metin; boşsa null. maxLength: yazılacağı sütunun sınırı.
    public static string? ParseText(string? value, int maxLength, string name)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length <= maxLength
            ? text
            : throw new McpException($"{name} en fazla {maxLength} karakter olabilir.");
    }

    // Normalize edilmiş girdilerin (kayıt) özeti: önizleme ile kaydın ve tekrar denemenin aynı girdiyi taşıdığını gösterir.
    public static string Fingerprint<TInput>(TInput input) where TInput : notnull
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input)));

    public static int ClampLimit(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
}
