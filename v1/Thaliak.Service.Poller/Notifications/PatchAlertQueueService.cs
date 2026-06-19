using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Serilog;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Common.Messages.Polling;
using Thaliak.Service.Poller.Webhooks;

namespace Thaliak.Service.Poller.Notifications;

public sealed class PatchAlertQueueService(ThaliakContext db, IConfiguration configuration)
{
    public async Task QueueEligiblePatchesAsync(IReadOnlyCollection<XivPatch> patches,
        PatchDiscoveryType discoveryType, DateTime detectedAtUtc, CancellationToken cancellationToken = default)
    {
        if (patches.Count == 0 || !ShouldNotify(discoveryType)) {
            return;
        }

        var suppressBoot = NotificationConfiguration.ShouldSuppressBootPatchAlerts(configuration);
        var queued = 0;

        foreach (var patch in patches) {
            await EnsureRepositoryLoadedAsync(patch, cancellationToken);

            if (patch.NotificationSentAtUtc is not null || patch.NotificationQueuedAtUtc is not null) {
                continue;
            }

            var repository = patch.RepoVersion.Repository;
            if (PatchRegion.FromServiceId(repository.ServiceId) is null) {
                Log.Warning("Skipping alert queue for unknown service id {ServiceId}", repository.ServiceId);
                continue;
            }

            if (suppressBoot && IsBootRepository(repository)) {
                Log.Information("Suppressing boot patch alert for {Repository} {Version}", repository.Name,
                    patch.RepoVersion.VersionString);
                continue;
            }

            patch.NotificationQueuedAtUtc = detectedAtUtc;
            patch.NotificationDiscoveryType = discoveryType.ToString();
            queued++;
        }

        if (queued > 0) {
            await db.SaveChangesAsync(cancellationToken);
            Log.Information("Queued {PatchCount} patches for delayed notification batching", queued);
        }
    }

    private bool ShouldNotify(PatchDiscoveryType discoveryType)
    {
        return discoveryType == PatchDiscoveryType.Offered ||
               NotificationConfiguration.ShouldNotifyScrapedPatches(configuration);
    }

    private async Task EnsureRepositoryLoadedAsync(XivPatch patch, CancellationToken cancellationToken)
    {
        if (patch.RepoVersion?.Repository is not null) {
            return;
        }

        if (patch.RepoVersion is null) {
            await db.Entry(patch)
                .Reference(p => p.RepoVersion)
                .LoadAsync(cancellationToken);
        }

        var repoVersion = patch.RepoVersion ??
                          throw new InvalidDataException($"Patch {patch.Id} has no repository version loaded");

        await db.Entry(repoVersion)
            .Reference(rv => rv.Repository)
            .LoadAsync(cancellationToken);
    }

    private static bool IsBootRepository(XivRepository repository)
    {
        return repository.Name.EndsWith("/boot", StringComparison.OrdinalIgnoreCase);
    }
}
