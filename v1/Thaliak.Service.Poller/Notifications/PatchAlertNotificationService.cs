using Thaliak.Common.Database.Models;
using Thaliak.Common.Messages.Polling;
using Thaliak.Service.Poller.Webhooks;

namespace Thaliak.Service.Poller.Notifications;

public sealed class PatchAlertNotificationService(
    IPatchDiscordAlertSender discordAlertSender,
    JsonWebhookService jsonWebhookService)
{
    public async Task SendPatchBatchAsync(string region, IReadOnlyCollection<XivPatch> patches,
        PatchDiscoveryType discoveryType, DateTime detectedAtUtc, DateTime quietWindowEndedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await discordAlertSender.SendBatchAsync(region, patches, discoveryType, quietWindowEndedAtUtc,
            cancellationToken);
        await jsonWebhookService.SendPatchBatchDiscoveredAsync(region, patches, discoveryType, detectedAtUtc,
            quietWindowEndedAtUtc, cancellationToken);
    }
}
