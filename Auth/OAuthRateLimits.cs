using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Kumburgaz.Web.Auth;

/// <summary>
/// Bütün adreslerden saatte açılabilecek istemci toplamı: çok IP'li bir saldırgan da anonim kayıt ucuyla veritabanını
/// şişiremez. IP sınırından sonra, yalnız gerçekten kaydedilecek istekte harcanır; tek IP'nin reddedilen istekleri tüketmez.
/// </summary>
public sealed class ClientRegistrationQuota(int permitsPerHour) : IDisposable
{
    private readonly FixedWindowRateLimiter _limiter = new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = permitsPerHour,
        Window = TimeSpan.FromHours(1),
        QueueLimit = 0
    });

    public bool TryTake()
    {
        using var lease = _limiter.AttemptAcquire();
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}

public static class OAuthRateLimits
{
    public const string RegisterPolicy = "oauth-register";

    private const int GlobalRegistrationsPerHour = 300;

    // Anonim kayıt ucu istemci başına saatte 20 kayıtla sınırlı; Cloudflare arkasında gerçek IP CF-Connecting-IP'de gelir.
    public static IServiceCollection AddKumburgazOAuthRateLimits(this IServiceCollection services)
        => services.AddSingleton(_ => new ClientRegistrationQuota(GlobalRegistrationsPerHour)).AddRateLimiter(o =>
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
