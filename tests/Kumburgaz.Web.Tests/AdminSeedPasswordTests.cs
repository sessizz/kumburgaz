using Kumburgaz.Web.Services;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class AdminSeedPasswordTests
{
    [Fact]
    public void Configured_password_is_used()
    {
        var result = AdminSeedPassword.Resolve("Gizli-Sifre-2026");

        Assert.Equal("Gizli-Sifre-2026", result.Password);
        Assert.False(result.Generated);
    }

    // Kaynak koddaki sabit "Admin123!" her yeni kurulumda bilinen bir yönetici
    // şifresi demekti. Yapılandırma yoksa rastgele, tahmin edilemez bir şifre üretilir.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_password_is_generated_randomly(string? configured)
    {
        var first = AdminSeedPassword.Resolve(configured);
        var second = AdminSeedPassword.Resolve(configured);

        Assert.True(first.Generated);
        Assert.NotEqual(AdminSeedPassword.LegacyDefault, first.Password);
        Assert.NotEqual(first.Password, second.Password);
        Assert.True(first.Password.Length >= 16);
        Assert.Contains(first.Password, char.IsDigit);
    }
}
