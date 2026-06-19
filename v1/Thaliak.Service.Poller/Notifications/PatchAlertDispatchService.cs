using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Serilog;
using Thaliak.Common.Database;
using Thaliak.Common.Database.Models;
using Thaliak.Common.Messages.Polling;
using Thaliak.Service.Poller.Webhooks;

namespace Thaliak.Service.Poller.Notifications;

public sealed class PatchAlertDispatchService(
    ThaliakContext db,
    IConfiguration configuration,
    PatchAlertNotificationService notificationService)
{
    public async Task<int> DispatchReadyBatchesAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var quietWindow = NotificationConfiguration.GetQuietWindow(configuration);
        var queuedPatches = await db.Patches
            .Include(p => p.RepoVersion)
            .ThenInclude(rv => rv.Repository)
            .Where(p => p.NotificationQueuedAtUtc != null && p.NotificationSentAtUtc == null)
            .ToListAsync(cancellationToken);

        var batches = queuedPatches
            .Select(p => new
            {
                Patch = p,
                Region = PatchRegion.FromServiceId(p.RepoVersion.Repository.ServiceId),
                DiscoveryType = GetDiscoveryType(p)
            })
            .Where(p => p.Region is not null)
            .GroupBy(p => new {p.Region, p.DiscoveryType});

        var dispatched = 0;
        foreach (var batch in batches) {
            var latestQueuedAt = batch.Max(p => p.Patch.NotificationQueuedAtUtc!.Value);
            var quietWindowEndedAt = latestQueuedAt.Add(quietWindow);
            if (quietWindowEndedAt > nowUtc) {
                continue;
            }

            var patches = batch.Select(p => p.Patch).ToArray();
            var detectedAt = patches.Min(p => p.NotificationQueuedAtUtc!.Value);

            Log.Information("Dispatching {PatchCount} queued patch alerts for {Region}", patches.Length,
                batch.Key.Region);

            await notificationService.SendPatchBatchAsync(batch.Key.Region!, patches, batch.Key.DiscoveryType,
                detectedAt, quietWindowEndedAt, cancellationToken);

            foreach (var patch in patches) {
                patch.NotificationSentAtUtc = nowUtc;
            }

            dispatched++;
        }

        if (dispatched > 0) {
            await db.SaveChangesAsync(cancellationToken);
        }

        return dispatched;
    }

    private static PatchDiscoveryType GetDiscoveryType(XivPatch patch)
    {
        return Enum.TryParse<PatchDiscoveryType>(patch.NotificationDiscoveryType, out var discoveryType)
            ? discoveryType
            : PatchDiscoveryType.Offered;
    }
}
