using Thaliak.Common.Database.Models;
using Thaliak.Common.Messages.Polling;

namespace Thaliak.Service.Poller.Notifications;

public interface IPatchDiscordAlertSender
{
    Task SendBatchAsync(string region, IReadOnlyCollection<XivPatch> patches, PatchDiscoveryType discoveryType,
        DateTime quietWindowEndedAtUtc, CancellationToken cancellationToken = default);
}
