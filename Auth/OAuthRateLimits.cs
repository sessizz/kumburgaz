using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Kumburgaz.Web.Auth;

public static class OAuthRateLimits
{
    public const string RegisterPolicy = "oauth-register";

    // Anonim kayıt ucu istemci başına saatte 20 kayıtla sınırlı; Cloudflare arkasında gerçek IP CF-Connecting-IP'de gelir.
    public static IServiceCollection AddKumburgazOAuthRateLimits(this IServiceCollection services)
        => services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(RegisterPolicy, ctx =>
            {
                var key = ctx.Request.Headers["CF-Connecting-IP"].ToString();
                if (string.IsNullOrWhiteSpace(key))
                    key = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0
                });
            });
        });
}
