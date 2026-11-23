using Kumburgaz.Web.Data;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Kumburgaz.Web.Auth;

public static class OAuthServerSetup
{
    private const string DefaultIssuer = "https://kumburgaz.bahceliler.net/";

    /// <summary>
    /// claude.ai bağlayıcısı için OAuth 2.1 yetkilendirme sunucusu: authorization code + PKCE (S256),
    /// refresh token ve /mcp için token doğrulaması. Authorize/token uçları passthrough'tur
    /// (kullanıcı girişi ve onay uygulamanın kendi controller'ında); keşif belgesi dinamik
    /// istemci kaydı (DCR) adresini de ilan eder.
    /// </summary>
    public static IServiceCollection AddKumburgazOAuthServer(this IServiceCollection services, IConfiguration config)
    {
        var issuer = config.GetOAuthIssuer();

        services.AddOpenIddict()
            .AddCore(options => options
                .UseEntityFrameworkCore()
                .UseDbContext<ApplicationDbContext>())
            .AddServer(options =>
            {
                options.SetIssuer(issuer);
                options.SetAuthorizationEndpointUris("connect/authorize");
                options.SetTokenEndpointUris("connect/token");

                options.AllowAuthorizationCodeFlow()
                    .AllowRefreshTokenFlow();
                options.RequireProofKeyForCodeExchange();
                // AllowAuthorizationCodeFlow "plain" yöntemini de açar; yalnız S256 kabul edilir.
                options.Configure(server => server.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain));
                options.RegisterScopes(OAuthConstants.Scope);
                // RFC 8707: claude.ai authorize/token isteğinde resource=.../mcp gönderir. OpenIddict
                // kayıtlı olmayan resource'u invalid_target ile reddeder; istemci izinleri de DCR'de verilir.
                options.RegisterResources(config.GetAcceptedMcpResources());

                options.SetAccessTokenLifetime(OAuthConstants.AccessTokenLifetime);
                options.SetRefreshTokenLifetime(OAuthConstants.RefreshTokenLifetime);

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();

                // OpenIddict DCR ucu sunmaz; kayıt ucu uygulamada (connect/register), keşif belgesine elle eklenir.
                options.AddEventHandler<HandleConfigurationRequestContext>(builder => builder.UseInlineHandler(context =>
                {
                    context.Metadata["registration_endpoint"] = new Uri(issuer, "connect/register").AbsoluteUri;
                    // DCR secret'sız (PKCE'li) istemci de açar; OpenIddict bu yöntemi kendiliğinden ilan etmez.
                    context.TokenEndpointAuthenticationMethods.Add(ClientAuthenticationMethods.None);
                    return default;
                }));
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                // Token yalnız Authorization başlığından: adres ve form gövdesi günlüklere, geçmişe düşer.
                options.UseAspNetCore()
                    .DisableAccessTokenExtractionFromQueryString()
                    .DisableAccessTokenExtractionFromBodyForm();
                options.AddAudiences(config.GetMcpResource());
                // İptal edilen bağlantının token'ı süresi dolmadan geçersiz olsun (bkz. ConnectedAppsController);
                // aksi halde yalnız imza, süre ve audience'a bakılır. Bağlantının kendisi de denetlenir: iptal sırasında
                // yenilenen token'ın kaydı iptal edilmemiş olabilir.
                options.EnableTokenEntryValidation();
                options.EnableAuthorizationEntryValidation();
            });

        // İmzalama/şifreleme anahtarları veritabanından (bkz. OAuthKeyStore).
        services.ConfigureOptions<OAuthKeyStore>();
        services.AddScoped<OAuthPrincipalFactory>();

        return services;
    }

    // /mcp'nin kanonik kaynak adresi; access token'ın audience'ı her zaman budur.
    public static string GetMcpResource(this IConfiguration config)
        => new Uri(config.GetOAuthIssuer(), "mcp").AbsoluteUri;

    // İstemcinin "resource" olarak gönderebileceği değerler: kanonik adres ve sonunda "/" olan hali.
    public static string[] GetAcceptedMcpResources(this IConfiguration config)
    {
        var resource = config.GetMcpResource();
        return [resource, resource + "/"];
    }

    // OAuth:Issuer (varsayılan canlı adres), her zaman "/" ile biter; uç adresleri buna göre çözülür.
    public static Uri GetOAuthIssuer(this IConfiguration config)
    {
        var value = config["OAuth:Issuer"];
        if (string.IsNullOrWhiteSpace(value))
        {
            value = DefaultIssuer;
        }

        if (!value.EndsWith('/'))
        {
            value += "/";
        }

        return new Uri(value, UriKind.Absolute);
    }
}
