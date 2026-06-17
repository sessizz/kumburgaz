using System.Globalization;

namespace Kumburgaz.Web.Services;

public static class FlexibleDecimalParser
{
    public static bool TryParse(string? value, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim()
            .Replace("₺", string.Empty)
            .Replace("TL", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty)
            .Replace("\u00a0", string.Empty)
            .Replace("\u202f", string.Empty);

        var commaIndex = normalized.LastIndexOf(',');
        var dotIndex = normalized.LastIndexOf('.');
        if (commaIndex >= 0 && dotIndex >= 0)
        {
            normalized = commaIndex > dotIndex
                ? normalized.Replace(".", string.Empty).Replace(',', '.')
                : normalized.Replace(",", string.Empty);
        }
        else if (commaIndex >= 0)
        {
            normalized = normalized.Replace(".", string.Empty).Replace(',', '.');
        }
        else if (dotIndex >= 0 && normalized.IndexOf('.') != dotIndex)
        {
            var decimals = normalized.Length - dotIndex - 1;
            normalized = decimals == 3
                ? normalized.Replace(".", string.Empty)
                : $"{normalized[..dotIndex].Replace(".", string.Empty)}.{normalized[(dotIndex + 1)..]}";
        }

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }
}
