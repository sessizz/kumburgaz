using System.Text.Encodings.Web;
using System.Text.Json;
using Kumburgaz.Web.Auth;
using Kumburgaz.Web.Mcp.Tools;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace Kumburgaz.Web.Mcp;

public static class McpSetup
{
    private const string ServerName = "Kumburgaz";

    // Araç sonucu Claude'a JSON metni olarak gider; varsayılan kodlayıcı Türkçe harfleri Unicode kaçış
    // dizilerine çevirir. Sonuç HTML'e gömülmediği için gevşek kodlayıcı güvenlidir. Her WithTools bunu alır.
    private static readonly JsonSerializerOptions ToolJsonOptions = new(McpJsonUtilities.DefaultOptions)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// claude.ai bağlayıcısının MCP sunucusu. Token'ı OpenIddict doğrular; token'sız isteğe MCP şeması
    /// 401 ve korumalı kaynak metadatası adresiyle yanıt verir. Varsayılan şema (Identity çerezi) değişmez:
    /// /mcp yalnız bearer token kabul eder.
    /// </summary>
    public static IServiceCollection AddKumburgazMcp(this IServiceCollection services, IConfiguration config)
    {
        services.AddAuthentication().AddMcp(options =>
        {
            options.ForwardAuthenticate = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            options.ResourceMetadata = new()
            {
                Resource = config.GetMcpResource(),
                AuthorizationServers = { config.GetOAuthIssuer().AbsoluteUri },
                ScopesSupported = [OAuthConstants.Scope],
                ResourceName = ServerName
            };
        });

        services.AddAuthorizationBuilder().AddPolicy(OAuthConstants.McpPolicy, policy => policy
            .AddAuthenticationSchemes(McpAuthenticationDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.HasScope(OAuthConstants.Scope))
            .AddRequirements(new ConnectorEligibilityRequirement()));
        services.AddScoped<IAuthorizationHandler, ConnectorEligibilityHandler>();

        services.AddSingleton<McpLinks>();

        services.AddMcpServer(options =>
            {
                options.ServerInfo = new() { Name = ServerName, Version = "1.0" };
                options.ServerInstructions = McpInstructions.Text;
            })
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .AddAuthorizationFilters()
            .WithTools<CategoryTools>(ToolJsonOptions)
            .WithTools<UnitTools>(ToolJsonOptions)
            .WithTools<FinanceTools>(ToolJsonOptions)
            .WithResources<McpGuideResource>();

        return services;
    }

    public static void MapKumburgazMcp(this WebApplication app)
        => app.MapMcp("/mcp").RequireAuthorization(OAuthConstants.McpPolicy);
}
