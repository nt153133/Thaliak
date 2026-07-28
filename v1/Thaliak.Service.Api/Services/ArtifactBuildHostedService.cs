using Microsoft.Extensions.Options;
using Thaliak.Service.Api.Artifacts;

namespace Thaliak.Service.Api.Services;

public sealed class ArtifactBuildHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<ArtifactOptions> options,
    ILogger<ArtifactBuildHostedService> logger) : BackgroundService
{
    private readonly ArtifactOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) {
            logger.LogInformation("Artifact generator is disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(30, _options.PollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested) {
            try {
                using var scope = scopeFactory.CreateScope();
                var buildService = scope.ServiceProvider.GetRequiredService<ArtifactBuildService>();
                await buildService.BuildAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
            catch (Exception ex) {
                logger.LogError(ex, "Artifact generator pass failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
