using System.Security.Cryptography;
using Kumburgaz.Web.Data;
using Kumburgaz.Web.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace Kumburgaz.Web.Auth;

/// <summary>
/// OpenIddict'in imzalama (RS256) ve şifreleme (RSA-OAEP + A256CBC-HS512) anahtarlarını
/// OAuthKeys tablosundan yükler; yoksa RSA 2048 anahtar üretip kaydeder. Özel anahtar PEM'i
/// Data Protection ile korunur (anahtar halkası zaten veritabanında). Böylece deploy'lar
/// arasında aynı anahtarlar kullanılır ve verilmiş token'lar geçerli kalır.
/// OpenIddict'in kendi post-configure doğrulamasından önce çalışır.
/// </summary>
public sealed class OAuthKeyStore(IServiceScopeFactory scopeFactory, IDataProtectionProvider dataProtectionProvider)
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

    private static RsaSecurityKey LoadOrCreate(ApplicationDbContext db, IDataProtector protector, string use)
    {
        var stored = db.OAuthKeys
            .Where(x => x.Use == use)
            .OrderBy(x => x.Id)
            .FirstOrDefault();

        RSA rsa;
        if (stored is null)
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
        else
        {
            rsa = RSA.Create();
            rsa.ImportFromPem(protector.Unprotect(stored.ProtectedPrivateKeyPem));
        }

        var key = new RsaSecurityKey(rsa);
        // Anahtar kimliği (kid) açık anahtardan türetilir (RFC 7638 parmak izi): her açılışta aynı çıkar.
        key.KeyId = Base64UrlEncoder.Encode(key.ComputeJwkThumbprint());
        return key;
    }
}
