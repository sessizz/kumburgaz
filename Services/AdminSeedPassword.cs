using System.Security.Cryptography;

namespace Kumburgaz.Web.Services;

/// <summary>
/// İlk kurulumda oluşturulan yönetici hesabının şifresi. Yapılandırmada
/// <c>Seed:AdminPassword</c> verilmemişse rastgele üretilir (eskiden kaynak koddaki
/// sabit "Admin123!" kullanılıyordu).
/// </summary>
public static class AdminSeedPassword
{
    public const string LegacyDefault = "Admin123!";

    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";

    public static (string Password, bool Generated) Resolve(string? configured)
        => string.IsNullOrWhiteSpace(configured)
            ? (Generate(), true)
            : (configured, false);

    private static string Generate()
    {
        // 18 harf/rakam + en az 2 rakam (Identity ayarı RequireDigit = true).
        var chars = new char[20];
        for (var i = 0; i < 18; i++)
        {
            var pool = i % 6 == 5 ? Digits : Alphabet + Digits;
            chars[i] = pool[RandomNumberGenerator.GetInt32(pool.Length)];
        }

        chars[18] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        chars[19] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        return new string(chars);
    }
}
