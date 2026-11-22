using System.Text;

namespace Kumburgaz.Web.Services;

// Arama anahtarı: küçük harf, Türkçe harfler ASCII, harf/rakam dışı karakterler ve "blok" kelimesi atılır.
// Böylece "a blok 5", "A-5" ve "A5" aynı anahtara, "Şahin" ile "SAHIN" aynı anahtara iner.
public static class TurkishSearchNormalizer
{
    private const string BlockWord = "blok";

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var result = new StringBuilder(value.Length);
        var word = new StringBuilder();

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                word.Append(Fold(ch));
            }
            else
            {
                Flush(word, result);
            }
        }

        Flush(word, result);
        return result.ToString();
    }

    private static void Flush(StringBuilder word, StringBuilder result)
    {
        if (word.Length > 0 && !word.Equals(BlockWord))
        {
            result.Append(word);
        }

        word.Clear();
    }

    private static char Fold(char ch) => ch switch
    {
        'İ' or 'I' or 'ı' or 'î' or 'Î' => 'i',
        'Ş' or 'ş' => 's',
        'Ğ' or 'ğ' => 'g',
        'Ü' or 'ü' or 'û' or 'Û' => 'u',
        'Ö' or 'ö' => 'o',
        'Ç' or 'ç' => 'c',
        'â' or 'Â' => 'a',
        _ => char.ToLowerInvariant(ch)
    };
}
