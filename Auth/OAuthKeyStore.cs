using System.Security.Cryptography;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace Kumburgaz.Web.Auth;

/// <summary>
/// OpenIddict'in imzalama (RS256) ve şifreleme (RSA-OAEP + A256CBC-HS512) anahtarlarını
/// OAuthKeys tablosundan yükler; yoksa RSA 2048 anahtar üretip kaydeder. Özel anahtar PEM'i
/// Data Protection ile korunur (anahtar halkası zaten veritabanında). Böylece deploy'lar
/// arasında aynı anahtarlar kullanılır ve verilmiş token'lar geçerli kalır. Kayıtlı anahtar açılamıyorsa (Data Protection
/// anahtarı kaybolmuş) site yine açılır: yeni anahtar üretilir, bağlayıcılar yeniden giriş yapar.
/// OpenIddict'in kendi post-configure doğrulamasından önce çalışır.
/// </summary>
public sealed class OAuthKeyStore(
    IServiceScopeFactory scopeFactory, IDataProtectionProvider dataProtectionProvider, ILogger<OAuthKeyStore> logger)
    : IConfigureOptions<OpenIddictServerOptions>
{
    private const string ProtectorPurpose = "Kumburgaz.OAuthKeys";
    private const string SigningUse = "sig";
    private const string EncryptionUse = "enc";
    private const int KeySizeInBits = 2048;

    public void Configure(OpenIddictServerOptions options)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);

        var signingKey = LoadOrCreate(db, protector, SigningUse);
        options.SigningCredentials.Add(new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));

        var encryptionKey = LoadOrCreate(db, protector, EncryptionUse);
        options.EncryptionCredentials.Add(new EncryptingCredentials(
            encryptionKey, SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512));
    }

    private RsaSecurityKey LoadOrCreate(ApplicationDbContext db, IDataProtector protector, string use)
    {
        // En yeni açılabilen anahtar; açılamayan satırlara dokunulmaz.
        var rsa = db.OAuthKeys.AsNoTracking()
            .Where(x => x.Use == use)
            .OrderByDescending(x => x.Id)
            .ToList()
            .Select(stored => TryOpen(stored, protector))
            .FirstOrDefault(opened => opened is not null);

        if (rsa is null)
        {
            rsa = RSA.Create(KeySizeInBits);
            db.OAuthKeys.Add(new OAuthKey
            {
                Use = use,
                ProtectedPrivateKeyPem = protector.Protect(rsa.ExportPkcs8PrivateKeyPem()),
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var key = new RsaSecurityKey(rsa);
        // Anahtar kimliği (kid) açık anahtardan türetilir (RFC 7638 parmak izi): her açılışta aynı çıkar.
        key.KeyId = Base64UrlEncoder.Encode(key.ComputeJwkThumbprint());
        return key;
    }

    private RSA? TryOpen(OAuthKey stored, IDataProtector protector)
    {
        string pem;
        try
        {
            pem = protector.Unprotect(stored.ProtectedPrivateKeyPem);
        }
        catch (CryptographicException ex)
        {
            logger.LogCritical(ex,
                "OAuth anahtarı {Id} ({Use}) açılamadı, kullanılmıyor (Data Protection anahtarı kaybolmuş olabilir). "
                + "Açılabilen başka anahtar yoksa yenisi üretilir ve Claude bağlantıları yeniden giriş ister.",
                stored.Id, stored.Use);
            return null;
        }

        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }
}
