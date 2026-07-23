namespace Kumburgaz.Web.Services;

public class ConsistencyCheckHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<ConsistencyCheckHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                var next = now.Date.AddHours(2);
                if (next <= now) next = next.AddDays(1);
                await Task.Delay(next - now, stoppingToken);

                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ConsistencyCheckService>();
                await service.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Tutarlılık kontrolü başarısız oldu.");
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }
    }
}
