using Thaliak.Service.Poller.Installation;
using Xunit;

namespace Thaliak.Service.Poller.Tests.Installation;

public sealed class InstallationSignalTests
{
    [Fact]
    public async Task WaitAsync_CoalescesSignalsUntilQuietPeriod()
    {
        var signal = new InstallationSignal();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        signal.Notify();
        var waitTask = signal.WaitAsync(timeout.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        signal.Notify();
        await Task.Delay(TimeSpan.FromMilliseconds(200), timeout.Token);

        Assert.False(waitTask.IsCompleted);
        await waitTask;
    }
}
