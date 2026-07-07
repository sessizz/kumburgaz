namespace Kumburgaz.Web.Services;

public class BackupHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<BackupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(NextRunDelay(), stoppingToken);
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<BackupService>();
                await service.CreateBackupAsync("gunluk", stoppingToken);
                service.PruneOldBackups();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Günlük yedekleme başarısız oldu.");
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }
    }

    private TimeSpan NextRunDelay()
    {
        var configured = configuration["Backups:DailyTime"];
        var time = TimeOnly.TryParse(configured, out var parsed) ? parsed : new TimeOnly(3, 0);
        var now = DateTime.Now;
        var next = now.Date.Add(time.ToTimeSpan());
        if (next <= now)
        {
            next = next.AddDays(1);
        }

        return next - now;
    }
}
