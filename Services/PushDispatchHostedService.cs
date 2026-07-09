using System.Threading.Channels;
using Kumburgaz.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Kumburgaz.Web.Services;

public sealed record PushJob(string UserId, string Title, string? Body, string? LinkUrl);

/// <summary>
/// NotificationService bu kuyruga best-effort push isleri ekler; istegi bekletmez.
/// </summary>
public sealed class PushQueue
{
    private readonly Channel<PushJob> _channel = Channel.CreateUnbounded<PushJob>();

    public ChannelReader<PushJob> Reader => _channel.Reader;

    public void Enqueue(PushJob job) => _channel.Writer.TryWrite(job);
}

/// <summary>
/// Kuyruktaki push islerini arka planda tuketir. Push devre disi ise (VAPID anahtari yok)
/// islemi sessizce atlar; 404/410 donen abonelikleri otomatik siler.
/// </summary>
public sealed class PushDispatchHostedService(
    PushQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<PushDispatchHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await DispatchAsync(job, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Push dispatch başarısız (kullanıcı: {UserId}).", job.UserId);
            }
        }
    }

    private async Task DispatchAsync(PushJob job, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<PushSenderService>();
        if (!sender.Enabled)
        {
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var subscriptions = await db.PushSubscriptions
            .Where(x => x.UserId == job.UserId)
            .ToListAsync(ct);

        foreach (var subscription in subscriptions)
        {
            var result = await sender.SendAsync(subscription, job.Title, job.Body, job.LinkUrl, ct);
            if (result == PushSendResult.Gone)
            {
                db.PushSubscriptions.Remove(subscription);
            }
            else if (result == PushSendResult.Failed)
            {
                subscription.FailCount++;
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
