using System.Diagnostics;
using Microsoft.Extensions.Options;
using Serilog;

namespace Thaliak.Service.Poller.Installation;

public sealed class RegionalInstallationCoordinator(
    IServiceScopeFactory scopeFactory,
    InstallationSignal signal,
    IOptions<InstallationOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            Log.Information("Regional game installation is disabled");
            return;
        }

        Log.Information("Regional game installation enabled for {Regions}", options.Value.Regions);
        signal.Notify();

        while (!stoppingToken.IsCancellationRequested)
        {
            await signal.WaitAsync(stoppingToken);

            try
            {
                var stopwatch = Stopwatch.StartNew();
                await using var scope = scopeFactory.CreateAsyncScope();
                var installer = scope.ServiceProvider.GetRequiredService<RegionalInstallationService>();
                await installer.ReconcileAsync(stoppingToken);
                stopwatch.Stop();
                Log.Information(
                    "[INSTALL-TIMING] End-to-end installation reconciliation completed in {Elapsed}",
                    stopwatch.Elapsed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Regional game installation reconciliation failed");
            }
        }
    }
}
