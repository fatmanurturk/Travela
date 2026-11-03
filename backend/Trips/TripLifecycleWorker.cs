namespace Travela.Api.Trips;

public sealed class TripLifecycleWorker(IServiceScopeFactory scopes, ILogger<TripLifecycleWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            do
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<TripLifecycleService>().Synchronize(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception error) { log.LogError(error, "Sefer durumları güncellenemedi; bir sonraki döngüde tekrar denenecek."); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
