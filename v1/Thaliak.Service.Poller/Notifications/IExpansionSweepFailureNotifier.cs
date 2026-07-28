namespace Thaliak.Service.Poller.Notifications;

public interface IExpansionSweepFailureNotifier
{
    Task SendFailureAsync(
        string triggerVersion,
        string reason,
        DateTime failedAtUtc,
        CancellationToken cancellationToken = default);
}
