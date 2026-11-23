using Kumburgaz.Web.Auth;
using Kumburgaz.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    // Data Protection anahtar halkası kaybolursa eski anahtar açılamaz: site yine açılır, yeni anahtarla devam edilir
    // (bağlayıcılar yeniden giriş yapar). Açılamayan satırlara dokunulmaz.
    [Fact]
    public async Task Unreadable_keys_are_replaced_without_touching_the_old_rows()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
        await using var provider = services.BuildServiceProvider();
        var lostDataProtection = new EphemeralDataProtectionProvider();
        var lost = Load(provider, lostDataProtection);
        var oldRows = await ReadRowsAsync(provider);
        var dataProtection = new EphemeralDataProtectionProvider();
        var log = new ListLogger();

        var replaced = Load(provider, dataProtection, log);

        Assert.NotEqual(Assert.Single(lost.SigningCredentials).Key.KeyId, Assert.Single(replaced.SigningCredentials).Key.KeyId);
        Assert.NotEqual(Assert.Single(lost.EncryptionCredentials).Key.KeyId, Assert.Single(replaced.EncryptionCredentials).Key.KeyId);
        Assert.Equal(2, log.Entries.Count(x => x.Level == LogLevel.Critical));
        var rows = await ReadRowsAsync(provider);
        Assert.Equal(4, rows.Count);
        Assert.Equal(oldRows, rows.Take(2));
        // Özel anahtar günlüğe yazılmaz.
        Assert.All(log.Entries, x => Assert.All(rows, row => Assert.DoesNotContain(row.Pem, x.Message)));

        // Sonraki açılışlar yeni anahtarı kullanır; bir daha anahtar üretilmez, uyarı da tekrarlanmaz.
        var again = new ListLogger();
        var reloaded = Load(provider, dataProtection, again);
        Assert.Equal(replaced.SigningCredentials[0].Key.KeyId, reloaded.SigningCredentials[0].Key.KeyId);
        Assert.Equal(replaced.EncryptionCredentials[0].Key.KeyId, reloaded.EncryptionCredentials[0].Key.KeyId);
        Assert.Empty(again.Entries);

        // Kayıp halka geri gelirse (ör. yedekten) onun açabildiği eski anahtar kullanılır, yenisi üretilmez.
        var restored = Load(provider, lostDataProtection);
        Assert.Equal(lost.SigningCredentials[0].Key.KeyId, restored.SigningCredentials[0].Key.KeyId);
        Assert.Equal(rows, await ReadRowsAsync(provider));
    }

    private static async Task<List<(int Id, string Use, string Pem, DateTime CreatedAt)>> ReadRowsAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().OAuthKeys.OrderBy(x => x.Id).ToListAsync();
        return rows.Select(x => (x.Id, x.Use, x.ProtectedPrivateKeyPem, x.CreatedAt)).ToList();
    }

    private static OpenIddictServerOptions Load(
        IServiceProvider provider, IDataProtectionProvider dataProtection, ILogger<OAuthKeyStore>? logger = null)
    {
        var options = new OpenIddictServerOptions();
        new OAuthKeyStore(
            provider.GetRequiredService<IServiceScopeFactory>(),
            dataProtection,
            logger ?? NullLogger<OAuthKeyStore>.Instance).Configure(options);
        return options;
    }

    private sealed class ListLogger : ILogger<OAuthKeyStore>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
