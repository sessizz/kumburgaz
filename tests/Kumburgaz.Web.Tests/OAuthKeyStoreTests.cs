using Kumburgaz.Web.Auth;
using Kumburgaz.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using Xunit;

namespace Kumburgaz.Web.Tests;

public class OAuthKeyStoreTests
{
    // Konteyner her deploy'da yeniden başlar; anahtarlar her açılışta yeniden üretilirse
    // verilmiş token'lar geçersizleşir ve claude.ai bağlantısı kopar.
    [Fact]
    public async Task Keys_are_created_once_and_reused()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        await using var provider = services.BuildServiceProvider();
        var dataProtection = new EphemeralDataProtectionProvider();

        var first = Load(provider, dataProtection);
        var second = Load(provider, dataProtection);

        var firstSigning = Assert.Single(first.SigningCredentials);
        var secondSigning = Assert.Single(second.SigningCredentials);
        Assert.False(string.IsNullOrEmpty(firstSigning.Key.KeyId));
        Assert.Equal(firstSigning.Key.KeyId, secondSigning.Key.KeyId);
        Assert.Equal(SecurityAlgorithms.RsaSha256, secondSigning.Algorithm);

        var firstEncryption = Assert.Single(first.EncryptionCredentials);
        var secondEncryption = Assert.Single(second.EncryptionCredentials);
        Assert.False(string.IsNullOrEmpty(firstEncryption.Key.KeyId));
        Assert.Equal(firstEncryption.Key.KeyId, secondEncryption.Key.KeyId);
        Assert.Equal(SecurityAlgorithms.RsaOAEP, secondEncryption.Alg);
        Assert.Equal(SecurityAlgorithms.Aes256CbcHmacSha512, secondEncryption.Enc);
        Assert.NotEqual(firstSigning.Key.KeyId, firstEncryption.Key.KeyId);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.OAuthKeys.CountAsync());
        // Korunmuş özel anahtar denetim kaydına kopyalanmaz.
        Assert.Equal(0, await db.AuditLogs.CountAsync());
    }

    private static OpenIddictServerOptions Load(IServiceProvider provider, IDataProtectionProvider dataProtection)
    {
        var options = new OpenIddictServerOptions();
        new OAuthKeyStore(provider.GetRequiredService<IServiceScopeFactory>(), dataProtection).Configure(options);
        return options;
    }
}
