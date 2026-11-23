using System.Globalization;
using Kumburgaz.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kumburgaz.Web.Tests.Integration;

/// <summary>
/// Uygulamayı gerçek Program.cs ile, geçici bir SQLite dosyası üzerinde ayağa kaldırır.
/// Program bağlantı dizesini ve OAuth issuer'ını builder.Build()'den önce okuduğu için
/// ortam değişkeniyle verilir. OpenIddict HTTPS dışı isteği reddettiğinden OAuth testleri
/// istemciyi BaseAddress = https://localhost ile kurar.
/// </summary>
public sealed class KumburgazAppFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://localhost/";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"kumburgaz-it-{Guid.NewGuid():N}.db");
    private readonly string? _previousConnectionString;
    private readonly string? _previousIssuer;

    // SQLite'ın hoş gördüğü, Npgsql'in reddettiği iç içe sorguları bütün entegrasyon testlerinde yakalar.
    public OpenReaderGuard OpenReaderGuard { get; } = new();

    public KumburgazAppFactory()
    {
        _previousConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        _previousIssuer = Environment.GetEnvironmentVariable("OAuth__Issuer");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", $"Data Source={_dbPath}");
        Environment.SetEnvironmentVariable("OAuth__Issuer", Issuer);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(OpenReaderGuard)));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _previousConnectionString);
        Environment.SetEnvironmentVariable("OAuth__Issuer", _previousIssuer);
        // Program.cs süreç geneli kültürü tr-TR yapar; diğer testleri etkilemesin.
        CultureInfo.DefaultThreadCurrentCulture = null;
        CultureInfo.DefaultThreadCurrentUICulture = null;
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { File.Delete(path); } catch (IOException) { }
        }

        // Uygulamanın yakalayıp yuttuğu ihlaller de (ör. arka plan servisi) koşuyu düşürür.
        if (OpenReaderGuard.Violations.Count > 0)
        {
            throw new InvalidOperationException(
                "Okuyucu açıkken başlatılan komutlar: " + string.Join(Environment.NewLine, OpenReaderGuard.Violations.Distinct()));
        }
    }
}

// Ortam değişkeni ve süreç kültürü paylaşıldığı için entegrasyon testleri
// diğer testlerle paralel koşmaz.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationCollection : ICollectionFixture<KumburgazAppFactory>
{
    public const string Name = "Integration";
}
