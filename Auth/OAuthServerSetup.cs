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

                options.SetAccessTokenLifetime(OAuthConstants.AccessTokenLifetime);
                options.SetRefreshTokenLifetime(OAuthConstants.RefreshTokenLifetime);

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough();

                // OpenIddict DCR ucu sunmaz; kayıt ucu uygulamada (connect/register), keşif belgesine elle eklenir.
                options.AddEventHandler<HandleConfigurationRequestContext>(builder => builder.UseInlineHandler(context =>
                {
                    context.Metadata["registration_endpoint"] = new Uri(issuer, "connect/register").AbsoluteUri;
                    return default;
                }));
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
                options.AddAudiences(new Uri(issuer, "mcp").AbsoluteUri);
            });

        // İmzalama/şifreleme anahtarları veritabanından (bkz. OAuthKeyStore).
        services.ConfigureOptions<OAuthKeyStore>();

        return services;
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
