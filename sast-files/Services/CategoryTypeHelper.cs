namespace Kumburgaz.Web.Services;

public static class CategoryTypeHelper
{
    public const string Gelir = "Gelir";
    public const string Gider = "Gider";

    public static string Normalize(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return Gider;
        }

        return type.Trim().ToLowerInvariant() switch
        {
            "income" => Gelir,
            "expense" => Gider,
            "gelir" => Gelir,
            "gider" => Gider,
            _ => type.Trim()
        };
    }

    public static string Display(string? type) => Normalize(type);
}
