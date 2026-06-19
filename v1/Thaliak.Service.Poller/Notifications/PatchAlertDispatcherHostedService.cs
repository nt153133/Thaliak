using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Thaliak.Service.Poller.Notifications;

public sealed class PatchAlertDispatcherHostedService(IServiceScopeFactory scopeFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (!stoppingToken.IsCancellationRequested) {
            try {
                using var scope = scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<PatchAlertDispatchService>();
                await dispatcher.DispatchReadyBatchesAsync(DateTime.UtcNow, stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                return;
            } catch (Exception ex) {
                Log.Warning(ex, "Error dispatching queued patch alerts");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }
}
